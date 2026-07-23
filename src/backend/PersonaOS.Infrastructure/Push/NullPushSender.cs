using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Push;

/// <summary>
/// Push adapter used when no provider is configured. Reports
/// <see cref="IsConfigured"/> = false so the dispatcher leaves reminders pending
/// (they fire once a real provider is set up) instead of marking them failed.
/// Replaced by the FCM adapter when Firebase credentials are present.
/// </summary>
public class NullPushSender(ILogger<NullPushSender> logger) : IPushSender
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<PushResult>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        logger.LogDebug("Push not configured; dropping notification '{Title}'.", title);
        IReadOnlyList<PushResult> results = tokens
            .Select(t => new PushResult(t, Success: false, Error: "Push notifications are not configured."))
            .ToList();
        return Task.FromResult(results);
    }
}
