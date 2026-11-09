namespace AbroApi.Auth;

public interface ISmsSender
{
    Task SendAsync(string phone, string message, CancellationToken ct = default);
}

/// Logs instead of sending. Replace with a real provider (e.g. Africa's Talking)
/// before launch.
public class ConsoleSmsSender(ILogger<ConsoleSmsSender> log) : ISmsSender
{
    public Task SendAsync(string phone, string message, CancellationToken ct = default)
    {
        log.LogInformation("SMS to {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}
