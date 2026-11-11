using System.Security.Claims;
using AbroApi.Common;
using AbroApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Notifications;

public record DeviceRequest(string FcmToken, string? Platform);
public record MarkReadRequest(List<Guid>? Ids, bool All);

public static class NotificationEndpoints
{
    private static Guid Me(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var n = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();

        n.MapGet("/", async (ClaimsPrincipal p, AbroDbContext db, CancellationToken ct, int limit = 50) =>
        {
            var me = Me(p);
            var items = await db.Notifications.Where(x => x.UserId == me)
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Clamp(limit, 1, 100))
                .Select(x => new { x.Id, x.Kind, x.Title, x.Body, x.Route, x.CreatedAt, x.ReadAt })
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        n.MapPost("/read", async (MarkReadRequest req, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var me = Me(p);
            var unread = db.Notifications.Where(x => x.UserId == me && x.ReadAt == null);
            if (!req.All)
            {
                var ids = req.Ids ?? [];
                unread = unread.Where(x => ids.Contains(x.Id));
            }
            await unread.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, DateTime.UtcNow), ct);
            return Results.NoContent();
        });

        var d = app.MapGroup("/api/v1/me/devices").WithTags("Me").RequireAuthorization();

        // Register (or move) this phone's FCM token to the signed-in user.
        d.MapPost("/", async (DeviceRequest req, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.FcmToken) || req.FcmToken.Length > 4096)
                return ApiError.Validation("fcmToken", "Token is required.");
            var me = Me(p);
            var token = req.FcmToken.Trim();
            var device = await db.Devices.FirstOrDefaultAsync(x => x.Token == token, ct);
            if (device is null)
                db.Devices.Add(new Device { UserId = me, Token = token, Platform = req.Platform ?? "android" });
            else
                device.UserId = me;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        d.MapDelete("/", async ([FromBody] DeviceRequest req, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var me = Me(p);
            await db.Devices.Where(x => x.UserId == me && x.Token == req.FcmToken)
                .ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
