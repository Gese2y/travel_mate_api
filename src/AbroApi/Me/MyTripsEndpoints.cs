using System.Security.Claims;
using AbroApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Me;

/// What the My Trips tab shows: everything I drive, organise or joined, and the
/// requests other people made that I still have to answer.
public static class MyTripsEndpoints
{
    private static Guid Me(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    private static string NameOf(User u) =>
        string.IsNullOrWhiteSpace(u.FirstName)
            ? "Someone"
            : $"{u.FirstName} {u.LastName}".Trim();

    private static string RideTitle(Ride r)
    {
        var stops = r.Stops.OrderBy(s => s.Order).ToList();
        var first = stops.First().Name.Split(',')[0].Trim();
        var last = stops.Last().Name.Split(',')[0].Trim();
        return $"{first} → {last} ride";
    }

    public static void MapMyTrips(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/me").WithTags("My trips").RequireAuthorization();

        // GET /me/trips?tab=upcoming|past
        g.MapGet("/trips", async (
            ClaimsPrincipal p, AbroDbContext db, CancellationToken ct, string tab = "upcoming") =>
        {
            var me = Me(p);
            var now = DateTime.UtcNow;
            var past = tab == "past";
            var items = new List<MyEntry>();

            var driving = await db.Rides.AsNoTracking()
                .Include(r => r.Stops).Include(r => r.Requests)
                .Where(r => r.DriverId == me && r.Status == "active")
                .ToListAsync(ct);
            foreach (var r in driving)
                items.Add(new MyEntry(r.Id, "ride", RideTitle(r), r.DepartureAt, "driver", "confirmed",
                    r.Stops.OrderBy(s => s.Order).First().Name,
                    r.Requests.Count(q => q.Status == RequestStatus.Pending),
                    $"/rides/{r.Id}"));

            var seats = await db.SeatRequests.AsNoTracking()
                .Include(q => q.Ride).ThenInclude(r => r.Stops)
                .Where(q => q.UserId == me && q.Ride.Status == "active" &&
                            (q.Status == RequestStatus.Pending || q.Status == RequestStatus.Accepted))
                .ToListAsync(ct);
            foreach (var q in seats)
                items.Add(new MyEntry(q.RideId, "ride", RideTitle(q.Ride), q.Ride.DepartureAt, "passenger",
                    q.Status == RequestStatus.Accepted ? "confirmed" : "waiting",
                    q.Ride.Stops.FirstOrDefault(s => s.Order == q.PickupStopOrder)?.Name,
                    0, $"/rides/{q.RideId}"));

            var organising = await db.Trips.AsNoTracking().Include(t => t.Requests)
                .Where(t => t.OrganiserId == me && t.Status == "active").ToListAsync(ct);
            foreach (var t in organising)
                items.Add(new MyEntry(t.Id, "trip", t.Title, t.LeavesAt, "organiser", "confirmed",
                    t.MeetingPoint, t.Requests.Count(q => q.Status == RequestStatus.Pending),
                    $"/trips/{t.Id}"));

            var joined = await db.TripRequests.AsNoTracking().Include(q => q.Trip)
                .Where(q => q.UserId == me && q.Trip.Status == "active" &&
                            (q.Status == RequestStatus.Pending || q.Status == RequestStatus.Accepted))
                .ToListAsync(ct);
            foreach (var q in joined)
                items.Add(new MyEntry(q.TripId, "trip", q.Trip.Title, q.Trip.LeavesAt, "member",
                    q.Status == RequestStatus.Accepted ? "confirmed" : "waiting",
                    q.Trip.MeetingPoint, 0, $"/trips/{q.TripId}"));

            var result = items
                .Where(i => past ? i.When < now : i.When >= now)
                .OrderBy(i => past ? 0 : 1).ThenBy(i => past ? -i.When.Ticks : i.When.Ticks)
                .ToList();
            return Results.Ok(result);
        });

        // GET /me/requests: pending requests on my rides and trips.
        g.MapGet("/requests", async (ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var me = Me(p);
            var now = DateTime.UtcNow;

            var seatReqs = await db.SeatRequests.AsNoTracking()
                .Include(q => q.User).Include(q => q.Ride).ThenInclude(r => r.Stops)
                .Where(q => q.Ride.DriverId == me && q.Status == RequestStatus.Pending &&
                            q.Ride.Status == "active" && q.Ride.DepartureAt >= now)
                .ToListAsync(ct);
            var tripReqs = await db.TripRequests.AsNoTracking()
                .Include(q => q.User).Include(q => q.Trip)
                .Where(q => q.Trip.OrganiserId == me && q.Status == RequestStatus.Pending &&
                            q.Trip.Status == "active" && q.Trip.LeavesAt >= now)
                .ToListAsync(ct);

            var list = seatReqs.Select(q => new MyRequest(q.Id, "ride", q.RideId,
                    RideTitle(q.Ride), q.Ride.DepartureAt,
                    new Requester(q.UserId, NameOf(q.User), q.User.Rating), q.Seats, q.Note, q.CreatedAt))
                .Concat(tripReqs.Select(q => new MyRequest(q.Id, "trip", q.TripId,
                    q.Trip.Title, q.Trip.LeavesAt,
                    new Requester(q.UserId, NameOf(q.User), q.User.Rating), 1, q.Note, q.CreatedAt)))
                .OrderBy(r => r.CreatedAt)
                .ToList();
            return Results.Ok(list);
        });
    }

    private record MyEntry(
        Guid Id, string Kind, string Title, DateTime When, string Role, string Status,
        string? Place, int PendingRequests, string Route);

    private record Requester(Guid Id, string Name, double Rating);

    private record MyRequest(
        Guid Id, string Kind, Guid ParentId, string Subject, DateTime When,
        Requester User, int Seats, string? Note, DateTime CreatedAt);
}
