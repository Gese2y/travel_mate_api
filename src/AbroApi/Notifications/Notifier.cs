using AbroApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Notifications;

public interface INotifier
{
    /// Adds to the user's inbox and pushes to their phones. Never throws: a
    /// failed push must not fail the request that caused it.
    Task SendAsync(Guid userId, string kind, string title, string body,
        string route, CancellationToken ct = default);

    /// Same message to many people, in one save and one push batch.
    Task SendManyAsync(IReadOnlyCollection<Guid> userIds, string kind, string title,
        string body, string route, CancellationToken ct = default);
}

public class Notifier(AbroDbContext db, PushSender push, ILogger<Notifier> log) : INotifier
{
    public async Task SendAsync(Guid userId, string kind, string title, string body,
        string route, CancellationToken ct = default)
    {
        try
        {
            db.Notifications.Add(new UserNotification
            {
                UserId = userId, Kind = kind, Title = title, Body = body, Route = route,
            });
            await db.SaveChangesAsync(ct);

            if (!push.Enabled) return;
            var tokens = await db.Devices.Where(d => d.UserId == userId)
                .Select(d => d.Token).ToListAsync(ct);
            var dead = await push.SendAsync(tokens, title, body, kind, route, ct);
            if (dead.Count > 0)
            {
                await db.Devices.Where(d => dead.Contains(d.Token)).ExecuteDeleteAsync(ct);
            }
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not notify user {UserId}.", userId);
        }
    }

    public async Task SendManyAsync(IReadOnlyCollection<Guid> userIds, string kind, string title,
        string body, string route, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return;
        try
        {
            db.Notifications.AddRange(userIds.Select(id => new UserNotification
            {
                UserId = id, Kind = kind, Title = title, Body = body, Route = route,
            }));
            await db.SaveChangesAsync(ct);

            if (!push.Enabled) return;
            var tokens = await db.Devices.Where(d => userIds.Contains(d.UserId))
                .Select(d => d.Token).ToListAsync(ct);
            var dead = await push.SendAsync(tokens, title, body, kind, route, ct);
            if (dead.Count > 0)
                await db.Devices.Where(d => dead.Contains(d.Token)).ExecuteDeleteAsync(ct);
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not notify {Count} users.", userIds.Count);
        }
    }
}
