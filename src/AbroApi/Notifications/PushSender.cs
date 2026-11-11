using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace AbroApi.Notifications;

/// Sends push notifications through Firebase Cloud Messaging.
///
/// Credentials come from `Firebase:ServiceAccountJson` (the whole service
/// account key JSON, e.g. env var `Firebase__ServiceAccountJson`). Without it
/// push is disabled and notifications only appear in the in-app inbox.
public class PushSender
{
    private readonly FirebaseMessaging? _messaging;
    private readonly ILogger<PushSender> _log;

    public PushSender(IConfiguration config, ILogger<PushSender> log)
    {
        _log = log;
        var json = config["Firebase:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(json))
        {
            _log.LogInformation("Push disabled: Firebase:ServiceAccountJson is not set.");
            return;
        }
        try
        {
            var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(json),
            });
            _messaging = FirebaseMessaging.GetMessaging(app);
            _log.LogInformation("Push enabled.");
        }
        catch (Exception e)
        {
            _log.LogError(e, "Push disabled: could not read the Firebase service account.");
        }
    }

    public bool Enabled => _messaging is not null;

    /// Returns the tokens FCM says are no longer valid, so they can be deleted.
    public async Task<List<string>> SendAsync(
        IReadOnlyList<string> tokens, string title, string body,
        string kind, string route, CancellationToken ct)
    {
        var dead = new List<string>();
        if (_messaging is null || tokens.Count == 0) return dead;

        var messages = tokens.Select(t => new Message
        {
            Token = t,
            Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
            Data = new Dictionary<string, string> { ["kind"] = kind, ["route"] = route },
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification { ChannelId = "abro_default" },
            },
        }).ToList();

        try
        {
            var result = await _messaging.SendEachAsync(messages, ct);
            for (var i = 0; i < result.Responses.Count; i++)
            {
                var r = result.Responses[i];
                if (r.IsSuccess) continue;
                var code = r.Exception?.MessagingErrorCode;
                if (code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                    dead.Add(tokens[i]);
                else
                    _log.LogWarning("Push failed: {Error}", r.Exception?.Message);
            }
        }
        catch (Exception e)
        {
            _log.LogError(e, "Push send failed.");
        }
        return dead;
    }
}
