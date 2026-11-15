using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AbroApi.Common;
using AbroApi.Data;
using AbroApi.Notifications;
using AbroApi.Trips;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Verification;

public record DecisionRequest(string Status, string? Reason);

public static class VerificationEndpoints
{
    private const int MaxPhotoBytes = 5 * 1024 * 1024;
    private static readonly string[] DocTypes = ["nationalId", "passport", "driverLicence"];

    private static Guid Me(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    private static async Task<(byte[]? Bytes, string? Type)> ReadPhoto(IFormFile? f, CancellationToken ct)
    {
        if (f is null || f.Length == 0 || f.Length > MaxPhotoBytes) return (null, null);
        await using var ms = new MemoryStream((int)f.Length);
        await f.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var type = TripEndpoints.SniffImageType(bytes);
        return type is null ? (null, null) : (bytes, type);
    }

    public static void MapVerification(this IEndpointRouteBuilder app)
    {
        MapUser(app);
        MapAdmin(app);
    }

    // ---- the signed-in user -------------------------------------------------

    private static void MapUser(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/me/verification").WithTags("Verification").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var me = Me(p);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me, ct);
            if (user is null) return ApiError.Result(401, "unauthorized", "Please sign in again.");
            var last = await db.IdVerifications.AsNoTracking()
                .Where(v => v.UserId == me).OrderByDescending(v => v.SubmittedAt)
                .Select(v => new { v.Reason }).FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                Status = user.IdStatus,
                Reason = user.IdStatus == "rejected" ? last?.Reason : null,
            });
        });

        g.MapPost("/", async (
            HttpRequest http, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var me = Me(p);
            var user = await db.Users.FindAsync([me], ct);
            if (user is null) return ApiError.Result(401, "unauthorized", "Please sign in again.");
            if (user.IdStatus == "approved")
                return ApiError.Result(409, "conflict", "Your ID is already verified.");
            if (user.IdStatus == "pending")
                return ApiError.Result(409, "conflict", "Your ID is already in review.");

            var form = await http.ReadFormAsync(ct);
            var docType = form["docType"].ToString();
            if (!DocTypes.Contains(docType))
                return ApiError.Validation("docType", "Choose a document type.");

            var (front, frontType) = await ReadPhoto(form.Files["front"], ct);
            var (back, backType) = await ReadPhoto(form.Files["back"], ct);
            var (selfie, selfieType) = await ReadPhoto(form.Files["selfie"], ct);
            if (front is null) return ApiError.Validation("front", "Add the front photo (JPEG/PNG/WebP, max 5 MB).");
            if (back is null) return ApiError.Validation("back", "Add the back photo (JPEG/PNG/WebP, max 5 MB).");
            if (selfie is null) return ApiError.Validation("selfie", "Add a selfie (JPEG/PNG/WebP, max 5 MB).");

            db.IdVerifications.Add(new IdVerification
            {
                UserId = me, DocType = docType,
                Front = front, FrontType = frontType!,
                Back = back, BackType = backType!,
                Selfie = selfie, SelfieType = selfieType!,
            });
            user.IdStatus = "pending";
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new { Status = "pending", Reason = (string?)null });
        }).DisableAntiforgery();
    }

    // ---- admin review (used by the admin site) ------------------------------

    private static bool IsAdmin(HttpContext http, IConfiguration cfg)
    {
        var key = cfg["Admin:Key"];
        if (string.IsNullOrWhiteSpace(key)) return false;
        var sent = http.Request.Headers["X-Admin-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sent)),
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static void MapAdmin(IEndpointRouteBuilder app)
    {
        // Called by the separate admin site (travel_mate_admin), which keeps the
        // key on its server. Every route needs the X-Admin-Key header.
        var g = app.MapGroup("/admin/api/verifications").AllowAnonymous().ExcludeFromDescription();

        g.MapGet("/", async (
            string? status, HttpContext http, IConfiguration cfg, AbroDbContext db, CancellationToken ct) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var s = status is "approved" or "rejected" ? status : "pending";
            var rows = await db.IdVerifications.AsNoTracking()
                .Where(v => v.Status == s)
                .OrderBy(v => v.SubmittedAt).Take(100)
                .Select(v => new
                {
                    v.Id, v.DocType, v.Status, v.Reason, v.SubmittedAt, v.ReviewedAt,
                    Name = (v.User.FirstName + " " + v.User.LastName).Trim(),
                    v.User.Phone,
                }).ToListAsync(ct);
            return Results.Ok(rows);
        });

        g.MapGet("/{id:guid}/{slot}", async (
            Guid id, string slot, HttpContext http, IConfiguration cfg, AbroDbContext db, CancellationToken ct) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            var v = await db.IdVerifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return slot switch
            {
                "front" => Results.Bytes(v.Front, v.FrontType),
                "back" => Results.Bytes(v.Back, v.BackType),
                "selfie" => Results.Bytes(v.Selfie, v.SelfieType),
                _ => Results.NotFound(),
            };
        });

        g.MapPost("/{id:guid}/decision", async (
            Guid id, DecisionRequest body, HttpContext http, IConfiguration cfg,
            AbroDbContext db, INotifier notifier, CancellationToken ct) =>
        {
            if (!IsAdmin(http, cfg)) return Results.Unauthorized();
            if (body.Status is not ("approved" or "rejected"))
                return ApiError.Validation("status", "Use approved or rejected.");
            var reason = body.Reason?.Trim();
            if (body.Status == "rejected" && string.IsNullOrEmpty(reason))
                return ApiError.Validation("reason", "Tell the user what to fix.");

            var v = await db.IdVerifications.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return ApiError.Result(404, "not_found", "Submission not found.");
            if (v.Status != "pending") return ApiError.Result(409, "conflict", "Already reviewed.");

            v.Status = body.Status;
            v.Reason = body.Status == "rejected" ? reason![..Math.Min(reason.Length, 300)] : null;
            v.ReviewedAt = DateTime.UtcNow;
            v.User.IdStatus = body.Status;
            await db.SaveChangesAsync(ct);

            if (body.Status == "approved")
                await notifier.SendAsync(v.UserId, "id_approved", "ID verified ✅",
                    "You can now offer rides and organise trips.", "/home", ct);
            else
                await notifier.SendAsync(v.UserId, "id_rejected", "ID needs another try",
                    v.Reason!, "/onboarding/verify", ct);
            return Results.Ok(new { v.Id, v.Status });
        });
    }
}
