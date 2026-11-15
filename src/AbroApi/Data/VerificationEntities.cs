namespace AbroApi.Data;

/// One ID submission. Photos are kept as bytes (like trip covers) and are only
/// ever served to the admin page, never publicly.
/// TODO: move to private object storage if volume grows.
public class IdVerification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// nationalId | passport | driverLicence
    public string DocType { get; set; } = "nationalId";
    public byte[] Front { get; set; } = [];
    public byte[] Back { get; set; } = [];
    public byte[] Selfie { get; set; } = [];
    public string FrontType { get; set; } = "image/jpeg";
    public string BackType { get; set; } = "image/jpeg";
    public string SelfieType { get; set; } = "image/jpeg";

    /// pending | approved | rejected
    public string Status { get; set; } = "pending";
    public string? Reason { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
