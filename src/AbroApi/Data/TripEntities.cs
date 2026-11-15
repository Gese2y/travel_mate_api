namespace AbroApi.Data;

/// A group trip organised by one user (e.g. "Entoto sunrise hike").
public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganiserId { get; set; }
    public User Organiser { get; set; } = null!;
    public string Title { get; set; } = "";

    /// "Day trip" | "Weekend"
    public string Kind { get; set; } = "Day trip";
    public string Destination { get; set; } = "";
    public string MeetingPoint { get; set; } = "";
    public DateTime LeavesAt { get; set; }
    public DateTime BackAt { get; set; }
    public int PricePerPerson { get; set; }

    /// Total people including the organiser.
    public int Capacity { get; set; }
    public string Description { get; set; } = "";

    /// approve | inviteOnly
    public string JoinPolicy { get; set; } = "approve";

    /// active | cancelled
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// Set when a cover photo is uploaded; used to bust image caches.
    public DateTime? CoverUpdatedAt { get; set; }

    public List<TripCostItem> CostItems { get; set; } = [];
    public List<TripRequest> Requests { get; set; } = [];

    /// The organiser plus everyone accepted.
    public int GoingCount => 1 + Requests.Count(r => r.Status == RequestStatus.Accepted);
    public int SpotsLeft => Capacity - GoingCount;
}

public class TripCostItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public string Label { get; set; } = "";
    public int Amount { get; set; }
}

public class TripRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string? Note { get; set; }

    /// pending | accepted | declined | cancelled (see RequestStatus)
    public string Status { get; set; } = RequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// The cover photo, kept in its own table so listing trips never loads image
/// bytes. TODO: move to object storage (e.g. Supabase Storage) when volume
/// grows; the endpoints would stay the same.
public class TripImage
{
    public Guid TripId { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public byte[] Data { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
