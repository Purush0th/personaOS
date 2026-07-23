namespace PersonaOS.Application.Common.Interfaces;

/// <summary>Outcome of a push attempt to a single device token.</summary>
/// <param name="Token">The token the attempt targeted.</param>
/// <param name="Success">Whether the provider accepted the message.</param>
/// <param name="TokenInvalid">
/// True when the provider reports the token is permanently unusable (uninstalled app,
/// rotated token). The caller should delete it rather than retry.
/// </param>
/// <param name="Error">Provider error text, when unsuccessful.</param>
public record PushResult(string Token, bool Success, bool TokenInvalid = false, string? Error = null);

/// <summary>
/// Push-notification port. The FCM adapter lives in Infrastructure; when push is not
/// configured, a no-op implementation reports "not configured" so reminder delivery
/// degrades gracefully instead of crashing the dispatcher.
/// </summary>
public interface IPushSender
{
    /// <summary>Whether a real provider is configured on this install.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one notification to each token; one result per token.</summary>
    Task<IReadOnlyList<PushResult>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default);
}
