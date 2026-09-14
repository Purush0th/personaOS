using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Push;

/// <summary>
/// Sends push notifications through Firebase Cloud Messaging. The only file that touches the
/// Firebase SDK; everything else sees <see cref="IPushSender"/>.
/// </summary>
public sealed class FcmPushSender : IPushSender, IDisposable
{
    /// <summary>FCM's limit per multicast request.</summary>
    private const int MaxTokensPerRequest = 500;

    private readonly FirebaseApp _app;
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(string serviceAccountJson, ILogger<FcmPushSender> logger)
    {
        _logger = logger;

        var credential = CredentialFactory
            .FromJson<ServiceAccountCredential>(serviceAccountJson)
            .ToGoogleCredential();

        // A uniquely named app per sender, never the SDK's global default. The credential can be
        // replaced at runtime from the admin page, and the Firebase SDK refuses to create a
        // second app under a name already taken — so each replacement gets its own name and the
        // old app is deleted in Dispose.
        _app = FirebaseApp.Create(
            new AppOptions { Credential = credential },
            $"personaos-push-{Guid.NewGuid():N}");
        _messaging = FirebaseMessaging.GetMessaging(_app);
    }

    public bool IsConfigured => true;

    public Task<IReadOnlyList<PushResult>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default) =>
        SendInBatchesAsync(tokens, data, ct, new AndroidConfig
        {
            // High priority so it wakes a dozing phone rather than waiting for the next
            // maintenance window.
            Priority = Priority.High,
            Notification = new AndroidNotification
            {
                ChannelId = "reminders",
                // Must be set explicitly. In FirebaseAdmin 3.6 EventTimestamp is a NON-nullable
                // DateTime, so leaving it unset still serialises `event_time` as
                // 0001-01-01T00:00:00Z — and Android then dated the notification about two
                // thousand years ago ("2032y" in the header). Confirmed by reflection on the SDK.
                EventTimestamp = DateTime.UtcNow,
            },
        }, new Notification { Title = title, Body = body });

    public Task<IReadOnlyList<PushResult>> SendDataAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default) =>
        // No Notification at all — a notification block would make Android draw it and bypass
        // the app. High priority is what lets the message wake a phone in Doze to act on it.
        SendInBatchesAsync(tokens, data, ct, new AndroidConfig { Priority = Priority.High }, notification: null);

    private async Task<IReadOnlyList<PushResult>> SendInBatchesAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken ct,
        AndroidConfig android,
        Notification? notification)
    {
        var results = new List<PushResult>(tokens.Count);

        foreach (var batch in tokens.Chunk(MaxTokensPerRequest))
        {
            // Registration tokens, not Firebase Installation IDs, despite `Tokens` being marked
            // obsolete in favour of `Fids`. FCM is migrating to FIDs, but the Flutter client
            // (firebase_messaging 16.6.0, checked 2026-09-14) has no FID API and can only
            // produce registration tokens — and the docs only promise the reverse compatibility
            // (the token field accepting FIDs), not FIDs accepting tokens. Move to `Fids` once
            // the client can register by FID; until then this is the path that delivers.
#pragma warning disable CS0618
            var message = new MulticastMessage
            {
                Tokens = batch,
                Notification = notification,
                Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Android = android,
            };
#pragma warning restore CS0618

            BatchResponse response;
            try
            {
                response = await _messaging.SendEachForMulticastAsync(message, ct);
            }
            catch (FirebaseMessagingException ex)
            {
                // The whole request failed (bad credentials, network). No token is known to be
                // bad, so report every one as a retryable failure — the dispatcher retries and
                // gives up after its attempt limit, rather than pruning devices that are fine.
                _logger.LogError(ex, "FCM request failed for {Count} token(s).", batch.Length);
                results.AddRange(batch.Select(t => new PushResult(t, Success: false, Error: ex.Message)));
                continue;
            }

            for (var i = 0; i < batch.Length; i++)
            {
                var sent = response.Responses[i];
                if (sent.IsSuccess)
                {
                    results.Add(new PushResult(batch[i], Success: true));
                    continue;
                }

                var code = sent.Exception?.MessagingErrorCode;
                results.Add(new PushResult(
                    batch[i],
                    Success: false,
                    TokenInvalid: IsPermanentlyInvalid(code),
                    Error: sent.Exception?.Message));
            }
        }

        return results;
    }

    public void Dispose() => _app.Delete();

    /// <summary>
    /// Whether FCM is saying the token itself is dead, so the caller should delete it.
    ///
    /// Kept narrow on purpose. Pruning is irreversible — a device whose token is wrongly deleted
    /// stops receiving anything until the app re-registers — so only codes that name the token
    /// count. <c>InvalidArgument</c> is deliberately excluded: it can mean a malformed payload,
    /// and treating that as a dead token would delete every registered device at once.
    /// </summary>
    internal static bool IsPermanentlyInvalid(MessagingErrorCode? code) => code is
        MessagingErrorCode.Unregistered         // app uninstalled, or token rotated away
        or MessagingErrorCode.SenderIdMismatch; // token belongs to a different Firebase project
}
