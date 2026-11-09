namespace AbroApi.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "abro-api";
    public string Audience { get; set; } = "abro-app";
    public string Key { get; set; } = "";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 30;
}

public class OtpOptions
{
    /// Dev only: when set, every code is this value and nothing is texted.
    public string? FixedCode { get; set; }
    public int ExpiryMinutes { get; set; } = 5;
    public int ResendSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
}
