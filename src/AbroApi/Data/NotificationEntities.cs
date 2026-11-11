namespace AbroApi.Data;

/// A phone that can receive push notifications for a user.
public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// FCM registration token. Unique: a token belongs to one user at a time.
    public string Token { get; set; } = "";
    public string Platform { get; set; } = "android";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// One entry in a user's notification inbox.
public class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// seat_request | seat_accepted | seat_declined | ride_cancelled | …
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    /// App path to open, e.g. `/rides/{id}` or `/my-trips`.
    public string Route { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
