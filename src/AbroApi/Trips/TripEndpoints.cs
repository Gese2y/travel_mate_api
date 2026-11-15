using System.Security.Claims;
using AbroApi.Common;
using AbroApi.Data;
using AbroApi.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Trips;

public static class TripEndpoints
{
    private const int MaxCoverBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan Addis = TimeSpan.FromHours(3);

    private static Guid Me(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    private static string NameOf(User u) =>
        string.IsNullOrWhiteSpace(u.FirstName) ? "Someone" : u.FirstName;

    private static IQueryable<Trip> Full(AbroDbContext db) => db.Trips
        .Include(t => t.Organiser).Include(t => t.CostItems)
        .Include(t => t.Requests).ThenInclude(r => r.User)
        .AsSplitQuery();

    /// JPEG, PNG or WebP, judged by the file's first bytes rather than by the
    /// content type the client claims.
    internal static string? SniffImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
            b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    public static void MapTrips(this IEndpointRouteBuilder app)
    {
        // Cover photos load through Image.network (no auth header) and carry
        // nothing sensitive: ids are unguessable GUIDs.
        app.MapGet("/api/v1/trips/{id:guid}/cover", async (
            Guid id, HttpContext http, AbroDbContext db, CancellationToken ct) =>
        {
            var img = await db.TripImages.AsNoTracking().FirstOrDefaultAsync(i => i.TripId == id, ct);
            if (img is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Bytes(img.Data, img.ContentType);
        }).WithTags("Trips").AllowAnonymous();

        var g = app.MapGroup("/api/v1/trips").WithTags("Trips").RequireAuthorization();

        g.MapGet("/", async (
            ClaimsPrincipal p, AbroDbContext db, CancellationToken ct,
            bool weekend = false, int? maxPrice = null, string? kind = null) =>
        {
            var me = Me(p);
            var q = Full(db).Where(t => t.Status == "active" && t.LeavesAt >= DateTime.UtcNow);
            if (maxPrice is not null) q = q.Where(t => t.PricePerPerson <= maxPrice);
            if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(t => t.Kind == kind);
            var trips = await q.OrderBy(t => t.LeavesAt).ToListAsync(ct);
            if (weekend)
                trips = trips.Where(t => (t.LeavesAt + Addis).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToList();
            return Results.Ok(trips.Select(t => TripMapper.ToDto(t, me)));
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var t = await Full(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            return t is null
                ? ApiError.Result(404, "not_found", "Trip not found.")
                : Results.Ok(TripMapper.ToDto(t, Me(p), detail: true));
        });

        g.MapPost("/", async (
            CreateTripRequest req, ClaimsPrincipal p, AbroDbContext db,
            IConfiguration cfg, CancellationToken ct) =>
        {
            var me = await db.Users.FindAsync([Me(p)], ct);
            if (me is null) return ApiError.Result(401, "unauthorized", "Please sign in again.");
            if (cfg.GetValue("Rules:RequireIdVerification", true) && me.IdStatus != "approved")
                return ApiError.Result(403, "id_verification_required", "Verify your ID to organise trips.");

            if (string.IsNullOrWhiteSpace(req.Title)) return ApiError.Validation("title", "Give the trip a name.");
            if (string.IsNullOrWhiteSpace(req.Destination)) return ApiError.Validation("destination", "Where is it going?");
            if (string.IsNullOrWhiteSpace(req.MeetingPoint)) return ApiError.Validation("meetingPoint", "Where do people meet?");
            if (!TripMapper.Kinds.Contains(req.Kind)) return ApiError.Validation("kind", "Use Day trip or Weekend.");
            if (req.LeavesAt <= DateTime.UtcNow) return ApiError.Validation("leavesAt", "Leaving time must be in the future.");
            if (req.BackAt < req.LeavesAt) return ApiError.Validation("backAt", "Back time must be after leaving.");
            if (req.PricePerPerson <= 0) return ApiError.Validation("pricePerPerson", "Enter a cost.");
            if (req.Capacity is < 2 or > 50) return ApiError.Validation("capacity", "Between 2 and 50 people.");
            var policy = req.JoinPolicy ?? "approve";
            if (policy is not ("approve" or "inviteOnly")) return ApiError.Validation("joinPolicy", "Use approve or inviteOnly.");
            var included = req.Included is { Count: > 0 }
                ? req.Included
                : [new TripCostInput("Trip cost", req.PricePerPerson)];
            if (included.Any(i => i.Amount < 0 || string.IsNullOrWhiteSpace(i.Label)))
                return ApiError.Validation("included", "Each item needs a name and an amount.");

            var trip = new Trip
            {
                OrganiserId = me.Id,
                Title = req.Title.Trim(),
                Kind = req.Kind,
                Destination = req.Destination.Trim(),
                MeetingPoint = req.MeetingPoint.Trim(),
                LeavesAt = req.LeavesAt.ToUniversalTime(),
                BackAt = req.BackAt.ToUniversalTime(),
                PricePerPerson = req.PricePerPerson,
                Capacity = req.Capacity,
                Description = req.Description?.Trim() ?? "",
                JoinPolicy = policy,
                CostItems = included.Select(i => new TripCostItem { Label = i.Label.Trim(), Amount = i.Amount }).ToList(),
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync(ct);
            trip.Organiser = me;
            return Results.Created($"/api/v1/trips/{trip.Id}", TripMapper.ToDto(trip, me.Id, detail: true));
        });

        // Upload or replace the photo of the place (multipart, field "file").
        g.MapPut("/{id:guid}/cover", async (
            Guid id, IFormFile? file, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return ApiError.Result(404, "not_found", "Trip not found.");
            if (trip.OrganiserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your trip.");
            if (file is null || file.Length == 0) return ApiError.Validation("file", "Choose a photo.");
            if (file.Length > MaxCoverBytes) return ApiError.Validation("file", "Photo must be 5 MB or smaller.");

            await using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var type = SniffImageType(bytes);
            if (type is null) return ApiError.Validation("file", "Use a JPEG, PNG or WebP photo.");

            var img = await db.TripImages.FirstOrDefaultAsync(i => i.TripId == id, ct);
            if (img is null) db.TripImages.Add(img = new TripImage { TripId = id });
            img.Data = bytes;
            img.ContentType = type;
            // Postgres keeps microseconds; trim so the version in the URL we hand
            // out matches the one we read back later.
            var now = DateTime.UtcNow;
            img.UpdatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
            trip.CoverUpdatedAt = img.UpdatedAt;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { CoverUrl = $"/api/v1/trips/{id}/cover?v={img.UpdatedAt.Ticks}" });
        }).DisableAntiforgery();

        g.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal p, AbroDbContext db, INotifier notifier, CancellationToken ct) =>
        {
            var t = await Full(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return ApiError.Result(404, "not_found", "Trip not found.");
            if (t.OrganiserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your trip.");
            t.Status = "cancelled";
            await db.SaveChangesAsync(ct);
            foreach (var r in t.Requests.Where(r => r.Status is RequestStatus.Pending or RequestStatus.Accepted))
                await notifier.SendAsync(r.UserId, "trip_cancelled", "Trip cancelled",
                    $"{NameOf(t.Organiser)} cancelled “{t.Title}”.", "/explore", ct);
            return Results.NoContent();
        });

        // ---- join requests ----
        g.MapPost("/{id:guid}/requests", async (
            Guid id, TripJoinInput? body, ClaimsPrincipal p, AbroDbContext db,
            INotifier notifier, CancellationToken ct) =>
        {
            var me = Me(p);
            var t = await Full(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || t.Status != "active") return ApiError.Result(404, "not_found", "Trip not found.");
            if (t.OrganiserId == me) return ApiError.Result(409, "conflict", "You’re organising this trip.");
            if (t.JoinPolicy == "inviteOnly") return ApiError.Result(403, "forbidden", "This trip is invite only.");
            if (t.SpotsLeft <= 0) return ApiError.Result(409, "seat_unavailable", "This trip is full.");
            if (t.Requests.Any(r => r.UserId == me && r.Status is RequestStatus.Pending or RequestStatus.Accepted))
                return ApiError.Result(409, "conflict", "You already asked to join this trip.");

            var q = new TripRequest { TripId = id, UserId = me, Note = body?.Note?.Trim() };
            db.TripRequests.Add(q);
            await db.SaveChangesAsync(ct);
            await db.Entry(q).Reference(x => x.User).LoadAsync(ct);
            await notifier.SendAsync(t.OrganiserId, "trip_request", "New join request",
                $"{NameOf(q.User)} wants to join “{t.Title}”.", "/my-trips", ct);
            return Results.Created($"/api/v1/trips/{id}/requests/{q.Id}", TripMapper.ToDto(q));
        });

        g.MapGet("/{id:guid}/requests", async (Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var t = await db.Trips.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return ApiError.Result(404, "not_found", "Trip not found.");
            if (t.OrganiserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your trip.");
            var list = await db.TripRequests.Include(q => q.User)
                .Where(q => q.TripId == id).OrderBy(q => q.CreatedAt).ToListAsync(ct);
            return Results.Ok(list.Select(TripMapper.ToDto));
        });

        g.MapPatch("/{tripId:guid}/requests/{id:guid}", async (
            Guid tripId, Guid id, TripDecision body, ClaimsPrincipal p,
            AbroDbContext db, INotifier notifier, CancellationToken ct) =>
        {
            if (body.Status is not (RequestStatus.Accepted or RequestStatus.Declined))
                return ApiError.Validation("status", "Use accepted or declined.");
            var t = await Full(db).FirstOrDefaultAsync(x => x.Id == tripId, ct);
            if (t is null) return ApiError.Result(404, "not_found", "Trip not found.");
            if (t.OrganiserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your trip.");
            var q = t.Requests.FirstOrDefault(x => x.Id == id);
            if (q is null) return ApiError.Result(404, "not_found", "Request not found.");
            if (q.Status != RequestStatus.Pending)
                return ApiError.Result(409, "conflict", "This request was already answered.");
            if (body.Status == RequestStatus.Accepted && t.SpotsLeft <= 0)
                return ApiError.Result(409, "seat_unavailable", "This trip is full.");

            q.Status = body.Status;
            await db.SaveChangesAsync(ct);
            if (q.Status == RequestStatus.Accepted)
                await notifier.SendAsync(q.UserId, "trip_accepted", "You’re in! ✅",
                    $"{NameOf(t.Organiser)} accepted you on “{t.Title}”. Meet at {t.MeetingPoint}.",
                    $"/trips/{t.Id}", ct);
            else
                await notifier.SendAsync(q.UserId, "trip_declined", "Request declined",
                    $"{NameOf(t.Organiser)} couldn’t add you to “{t.Title}”.", "/explore", ct);
            return Results.Ok(TripMapper.ToDto(q));
        });

        g.MapDelete("/{tripId:guid}/requests/{id:guid}", async (
            Guid tripId, Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var q = await db.TripRequests.FirstOrDefaultAsync(x => x.Id == id && x.TripId == tripId, ct);
            if (q is null) return ApiError.Result(404, "not_found", "Request not found.");
            if (q.UserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your request.");
            q.Status = RequestStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
