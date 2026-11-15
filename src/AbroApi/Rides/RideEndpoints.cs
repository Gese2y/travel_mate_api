using System.Security.Claims;
using AbroApi.Common;
using AbroApi.Data;
using AbroApi.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Rides;

public static class RideEndpoints
{
    // Addis Ababa is UTC+3 all year.
    private static readonly TimeSpan Addis = TimeSpan.FromHours(3);

    private static Guid Me(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    /// First name for notification text; accounts that skipped setup have none.
    private static string Who(User u) =>
        string.IsNullOrWhiteSpace(u.FirstName) ? "Someone" : u.FirstName;

    private static IQueryable<Ride> Full(AbroDbContext db) => db.Rides
        .Include(r => r.Driver).Include(r => r.Stops)
        .Include(r => r.CostItems).Include(r => r.Requests);

    /// Tells people who were recently within [Notifications:NearbyRideRadiusKm]
    /// (default 30) of a pickup point that a new ride is on offer. Never throws:
    /// the ride is already saved, so a failure here must not fail the request.
    private static async Task NotifyNearbyAsync(
        Ride ride, User driver, AbroDbContext db, IConfiguration cfg,
        INotifier notifier, CancellationToken ct)
    {
        try
        {
            var metres = cfg.GetValue("Notifications:NearbyRideRadiusKm", 30.0) * 1000;
            var recent = DateTime.UtcNow.AddDays(-7);
            var ids = new HashSet<Guid>();
            foreach (var stop in ride.Stops.Where(s => s.Kind != StopKind.DropOff))
            {
                var near = await db.Users
                    .Where(u => u.Id != driver.Id && u.LastLocation != null &&
                                u.LocationUpdatedAt >= recent &&
                                u.LastLocation.IsWithinDistance(stop.Location, metres))
                    .Select(u => u.Id).Take(200).ToListAsync(ct);
                ids.UnionWith(near);
            }
            if (ids.Count == 0) return;

            var ordered = ride.Stops.OrderBy(s => s.Order).ToList();
            string Short(string name) => name.Split(',')[0].Trim();
            var when = ride.DepartureAt.AddHours(3) // Addis is UTC+3
                .ToString("ddd H:mm", System.Globalization.CultureInfo.InvariantCulture);
            await notifier.SendManyAsync(ids.Take(200).ToList(), "ride_nearby", "New ride near you",
                $"{Who(driver)} is driving {Short(ordered.First().Name)} → {Short(ordered.Last().Name)}"
                + $" · {when} · ETB {ride.PricePerSeat:N0}/seat",
                $"/rides/{ride.Id}", ct);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Nearby-ride notification failed: {e.Message}");
        }
    }

    public static void MapRides(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/rides").WithTags("Rides").RequireAuthorization();

        g.MapGet("/", async (
            double fromLat, double fromLng, double toLat, double toLng,
            DateOnly date, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct,
            int seats = 1, double radiusKm = 3, string sort = "earliest") =>
        {
            var from = RideMapper.Pt(fromLat, fromLng);
            var to = RideMapper.Pt(toLat, toLng);
            var start = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - Addis, DateTimeKind.Utc); // Addis midnight → UTC
            var end = start.AddDays(1);
            var fromMetres = radiusKm * 1000;
            const double toMetres = 15_000; // destinations are areas, not exact stops

            var rides = await Full(db)
                .Where(r => r.Status == "active" && r.DepartureAt >= start && r.DepartureAt < end)
                .Where(r => r.Stops.Any(s => s.Kind != StopKind.DropOff &&
                                             s.Location.IsWithinDistance(from, fromMetres)))
                .Where(r => r.Stops.Any(s => s.Kind == StopKind.DropOff &&
                                             s.Location.IsWithinDistance(to, toMetres)))
                .AsSplitQuery()
                .ToListAsync(ct);

            var me = Me(p);
            var result = rides.Where(r => r.SeatsFree >= seats);
            result = sort == "cheapest"
                ? result.OrderBy(r => r.PricePerSeat)
                : result.OrderBy(r => r.DepartureAt);
            return Results.Ok(result.Select(r => RideMapper.ToDto(r, me, from)).ToList());
        });

        // Upcoming rides that pick up near [lat],[lng], nearest first.
        g.MapGet("/nearby", async (
            double lat, double lng, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct,
            double radiusKm = 25, int seats = 1, int limit = 20) =>
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180)
                return ApiError.Validation("lat", "Invalid coordinates.");
            radiusKm = Math.Clamp(radiusKm, 1, 200);
            limit = Math.Clamp(limit, 1, 50);

            var here = RideMapper.Pt(lat, lng);
            var now = DateTime.UtcNow;
            var metres = radiusKm * 1000;

            var rides = await Full(db)
                .Where(r => r.Status == "active" && r.DepartureAt >= now)
                .Where(r => r.Stops.Any(s => s.Kind != StopKind.DropOff &&
                                             s.Location.IsWithinDistance(here, metres)))
                .AsSplitQuery()
                .ToListAsync(ct);

            var me = Me(p);
            var result = rides
                .Where(r => r.SeatsFree >= seats)
                .Select(r => (Ride: r, Km: r.Stops.Where(s => s.Kind != StopKind.DropOff)
                    .Min(s => RideMapper.Km(s.Location, here))))
                .OrderBy(x => x.Km).ThenBy(x => x.Ride.DepartureAt)
                .Take(limit)
                .Select(x => RideMapper.ToDto(x.Ride, me, here))
                .ToList();
            return Results.Ok(result);
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var ride = await Full(db).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == id, ct);
            return ride is null
                ? ApiError.Result(404, "not_found", "Ride not found.")
                : Results.Ok(RideMapper.ToDto(ride, Me(p)));
        });

        g.MapPost("/", async (
            CreateRideRequest req, ClaimsPrincipal p, AbroDbContext db,
            IConfiguration cfg, INotifier notifier, CancellationToken ct) =>
        {
            var me = await db.Users.FindAsync([Me(p)], ct);
            if (me is null) return ApiError.Result(401, "unauthorized", "Please sign in again.");
            if (cfg.GetValue("Rules:RequireIdVerification", true) && me.IdStatus != "approved")
                return ApiError.Result(403, "id_verification_required",
                    "Verify your ID to offer rides.");

            if (req.SeatsTotal is < 1 or > 4)
                return ApiError.Validation("seatsTotal", "Seats must be between 1 and 4.");
            if (string.IsNullOrWhiteSpace(req.Car))
                return ApiError.Validation("car", "Car is required.");
            if (req.DepartureAt <= DateTime.UtcNow)
                return ApiError.Validation("departureAt", "Departure must be in the future.");
            if (req.Stops is not { Count: >= 2 } ||
                req.Stops[0].Kind != StopKind.Start || req.Stops[^1].Kind != StopKind.DropOff)
                return ApiError.Validation("stops", "Give a start, optional pickups, and a drop-off.");
            if (req.CostItems is not { Count: > 0 } || req.CostItems.Sum(c => c.Amount) <= 0 ||
                req.CostItems.Any(c => c.Amount < 0))
                return ApiError.Validation("costItems", "Enter the trip cost.");

            var ride = new Ride
            {
                DriverId = me.Id,
                Car = req.Car.Trim(),
                DepartureAt = req.DepartureAt.ToUniversalTime(),
                SeatsTotal = req.SeatsTotal,
                Stops = req.Stops.Select((s, i) => new RideStop
                {
                    Order = i,
                    Name = s.Name.Trim(),
                    Kind = s.Kind,
                    Time = (s.Time ?? req.DepartureAt).ToUniversalTime(),
                    Location = RideMapper.Pt(s.Lat, s.Lng),
                }).ToList(),
                CostItems = req.CostItems.Select(c => new RideCostItem
                    { Label = c.Label.Trim(), Amount = c.Amount }).ToList(),
            };
            db.Rides.Add(ride);
            await db.SaveChangesAsync(ct);
            ride.Driver = me;
            await NotifyNearbyAsync(ride, me, db, cfg, notifier, ct);
            return Results.Created($"/api/v1/rides/{ride.Id}", RideMapper.ToDto(ride, me.Id));
        });

        g.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal p, AbroDbContext db, INotifier notifier, CancellationToken ct) =>
        {
            var ride = await Full(db).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (ride is null) return ApiError.Result(404, "not_found", "Ride not found.");
            if (ride.DriverId != Me(p)) return ApiError.Result(403, "forbidden", "Not your ride.");
            ride.Status = "cancelled";
            await db.SaveChangesAsync(ct);

            var drop = ride.Stops.OrderBy(s => s.Order).Last().Name;
            foreach (var q in ride.Requests.Where(r =>
                         r.Status is RequestStatus.Pending or RequestStatus.Accepted))
                await notifier.SendAsync(q.UserId, "ride_cancelled", "Ride cancelled",
                    $"{Who(ride.Driver)} cancelled the ride to {drop}.", "/my-trips", ct);
            return Results.NoContent();
        });

        // ---- seat requests ----
        g.MapPost("/{id:guid}/requests", async (
            Guid id, SeatRequestInput req, ClaimsPrincipal p, AbroDbContext db,
            INotifier notifier, CancellationToken ct) =>
        {
            var me = Me(p);
            var ride = await Full(db).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (ride is null || ride.Status != "active")
                return ApiError.Result(404, "not_found", "Ride not found.");
            if (ride.DriverId == me)
                return ApiError.Result(409, "conflict", "You can’t request a seat on your own ride.");
            if (req.Seats < 1) return ApiError.Validation("seats", "Choose at least one seat.");
            if (req.Seats > ride.SeatsFree)
                return ApiError.Result(409, "seat_unavailable", "Not enough seats left on this ride.");
            if (!ride.Stops.Any(s => s.Order == req.PickupStopIndex && s.Kind != StopKind.DropOff))
                return ApiError.Validation("pickupStopIndex", "Choose a pickup point.");
            if (ride.Requests.Any(q => q.UserId == me &&
                                       q.Status is RequestStatus.Pending or RequestStatus.Accepted))
                return ApiError.Result(409, "conflict", "You already requested a seat on this ride.");

            var q = new SeatRequest
            {
                RideId = id, UserId = me, PickupStopOrder = req.PickupStopIndex,
                Seats = req.Seats, Note = req.Note?.Trim(),
            };
            db.SeatRequests.Add(q);
            await db.SaveChangesAsync(ct);
            await db.Entry(q).Reference(x => x.User).LoadAsync(ct);

            var drop = ride.Stops.OrderBy(s => s.Order).Last().Name;
            await notifier.SendAsync(ride.DriverId, "seat_request", "New seat request",
                $"{Who(q.User)} wants {q.Seats} seat{(q.Seats > 1 ? "s" : "")} to {drop}.",
                "/my-trips", ct);
            return Results.Created($"/api/v1/rides/{id}/requests/{q.Id}", RideMapper.ToDto(q));
        });

        g.MapGet("/{id:guid}/requests", async (Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var ride = await db.Rides.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (ride is null) return ApiError.Result(404, "not_found", "Ride not found.");
            if (ride.DriverId != Me(p)) return ApiError.Result(403, "forbidden", "Not your ride.");
            var list = await db.SeatRequests.Include(q => q.User)
                .Where(q => q.RideId == id).OrderBy(q => q.CreatedAt).ToListAsync(ct);
            return Results.Ok(list.Select(RideMapper.ToDto));
        });

        g.MapPatch("/{rideId:guid}/requests/{id:guid}", async (
            Guid rideId, Guid id, RequestDecision body, ClaimsPrincipal p,
            AbroDbContext db, INotifier notifier, CancellationToken ct) =>
        {
            if (body.Status is not (RequestStatus.Accepted or RequestStatus.Declined))
                return ApiError.Validation("status", "Use accepted or declined.");
            var ride = await Full(db).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == rideId, ct);
            if (ride is null) return ApiError.Result(404, "not_found", "Ride not found.");
            if (ride.DriverId != Me(p)) return ApiError.Result(403, "forbidden", "Not your ride.");
            var q = ride.Requests.FirstOrDefault(x => x.Id == id);
            if (q is null) return ApiError.Result(404, "not_found", "Request not found.");
            if (q.Status != RequestStatus.Pending)
                return ApiError.Result(409, "conflict", "This request was already answered.");
            if (body.Status == RequestStatus.Accepted && q.Seats > ride.SeatsFree)
                return ApiError.Result(409, "seat_unavailable", "Not enough seats left.");

            q.Status = body.Status;
            await db.SaveChangesAsync(ct);
            await db.Entry(q).Reference(x => x.User).LoadAsync(ct);

            if (q.Status == RequestStatus.Accepted)
            {
                var pickup = ride.Stops.FirstOrDefault(s => s.Order == q.PickupStopOrder)?.Name ?? "your pickup";
                await notifier.SendAsync(q.UserId, "seat_accepted", "Seat accepted ✅",
                    $"{Who(ride.Driver)} accepted your seat. Pickup: {pickup}.",
                    $"/rides/{ride.Id}", ct);
            }
            else
            {
                await notifier.SendAsync(q.UserId, "seat_declined", "Request declined",
                    $"{Who(ride.Driver)} couldn’t take you on this ride. Try another one.",
                    "/home", ct);
            }
            return Results.Ok(RideMapper.ToDto(q));
        });

        g.MapDelete("/{rideId:guid}/requests/{id:guid}", async (
            Guid rideId, Guid id, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var q = await db.SeatRequests.FirstOrDefaultAsync(x => x.Id == id && x.RideId == rideId, ct);
            if (q is null) return ApiError.Result(404, "not_found", "Request not found.");
            if (q.UserId != Me(p)) return ApiError.Result(403, "forbidden", "Not your request.");
            q.Status = RequestStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
