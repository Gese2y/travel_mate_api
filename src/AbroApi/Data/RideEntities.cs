using NetTopologySuite.Geometries;

namespace AbroApi.Data;

public static class StopKind
{
    public const string Start = "start";
    public const string Pickup = "pickup";
    public const string DropOff = "dropOff";
}

public static class RequestStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Cancelled = "cancelled";
}

public class Ride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DriverId { get; set; }
    public User Driver { get; set; } = null!;
    public string Car { get; set; } = "";
    public DateTime DepartureAt { get; set; }
    public int SeatsTotal { get; set; }

    /// active | cancelled
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RideStop> Stops { get; set; } = [];
    public List<RideCostItem> CostItems { get; set; } = [];
    public List<SeatRequest> Requests { get; set; } = [];

    public int TotalCost => CostItems.Sum(i => i.Amount);

    /// Cost sharing: total ÷ (passenger seats + driver).
    public int PricePerSeat => (int)Math.Round(TotalCost / (double)(SeatsTotal + 1));
    public int SeatsTaken => Requests.Where(r => r.Status == RequestStatus.Accepted).Sum(r => r.Seats);
    public int SeatsFree => SeatsTotal - SeatsTaken;
}

public class RideStop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RideId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = StopKind.Pickup;
    public DateTime Time { get; set; }

    /// WGS84 point (geography), so distances are in metres.
    public Point Location { get; set; } = null!;
}

public class RideCostItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RideId { get; set; }
    public string Label { get; set; } = "";
    public int Amount { get; set; }
}

public class SeatRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RideId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public int PickupStopOrder { get; set; }
    public int Seats { get; set; } = 1;
    public string? Note { get; set; }
    public string Status { get; set; } = RequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
