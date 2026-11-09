using AbroApi.Data;
using NetTopologySuite.Geometries;

namespace AbroApi.Rides;

public record StopInput(string Name, string Kind, DateTime? Time, double Lat, double Lng);
public record CostItemInput(string Label, int Amount);
public record CreateRideRequest(
    DateTime DepartureAt, int SeatsTotal, string Car,
    List<StopInput> Stops, List<CostItemInput> CostItems);
public record SeatRequestInput(int PickupStopIndex, int Seats, string? Note);
public record RequestDecision(string Status);

public static class RideMapper
{
    public static readonly GeometryFactory Gf = new(new PrecisionModel(), 4326);

    public static Point Pt(double lat, double lng) => Gf.CreatePoint(new Coordinate(lng, lat));

    /// Great-circle distance in km. (NTS in-memory Distance is planar degrees.)
    public static double Km(Point a, Point b)
    {
        const double R = 6371;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Y - a.Y);
        var dLng = Rad(b.X - a.X);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(a.Y)) * Math.Cos(Rad(b.Y)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }

    /// Shapes a ride like docs/api-contract.md. [from] adds pickup distance.
    public static object ToDto(Ride r, Guid? me, Point? from = null)
    {
        var ordered = r.Stops.OrderBy(s => s.Order).ToList();
        var pickup = from is null
            ? null
            : ordered.Where(s => s.Kind != StopKind.DropOff)
                .OrderBy(s => Km(s.Location, from)).FirstOrDefault();
        var mine = me is null ? null : r.Requests
            .Where(q => q.UserId == me && q.Status is RequestStatus.Pending or RequestStatus.Accepted)
            .OrderByDescending(q => q.CreatedAt).FirstOrDefault();

        return new
        {
            r.Id,
            Driver = new
            {
                r.Driver.Id,
                Name = $"{r.Driver.FirstName} {r.Driver.LastName}".Trim(),
                Rating = r.Driver.Rating,
                RidesCount = 0, // TODO: completed rides once trips can finish
                JoinedYear = r.Driver.CreatedAt.Year,
                IdVerified = r.Driver.IdStatus == "approved",
                LicenceChecked = r.Driver.IdStatus == "approved",
            },
            r.Car,
            r.DepartureAt,
            Stops = ordered.Select(s => new
            {
                s.Name, s.Time, s.Kind, Lat = s.Location.Y, Lng = s.Location.X,
                DistanceKm = from is null || s.Kind == StopKind.DropOff
                    ? (double?)null : Math.Round(Km(s.Location, from), 1),
            }),
            r.SeatsTotal,
            r.SeatsFree,
            CostItems = r.CostItems.Select(c => new { c.Label, c.Amount }),
            r.TotalCost,
            r.PricePerSeat,
            PickupLabel = pickup?.Name,
            PickupDistanceKm = pickup is null ? (double?)null : Math.Round(Km(pickup.Location, from!), 1),
            MyRequest = mine is null ? null : new { mine.Id, mine.Status },
        };
    }

    public static object ToDto(SeatRequest q) => new
    {
        q.Id, q.RideId,
        User = new
        {
            q.User.Id,
            Name = $"{q.User.FirstName} {q.User.LastName}".Trim(),
            q.User.Rating,
            IdVerified = q.User.IdStatus == "approved",
        },
        PickupStopIndex = q.PickupStopOrder, q.Seats, q.Note, q.Status, q.CreatedAt,
    };
}
