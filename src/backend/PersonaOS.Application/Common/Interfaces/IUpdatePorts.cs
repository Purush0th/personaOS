namespace PersonaOS.Application.Common.Interfaces;

/// <summary>A published release of PersonaOS: the server and web, or (<paramref name="IsApp"/>) the phone app.</summary>
/// <param name="Version">The tag without its "v" or "mobile-v", e.g. "0.1.0-alpha.9".</param>
/// <param name="AppDownloadUrl">The phone app attached to the release, when there is one.</param>
/// <param name="IsApp">A phone app release (tag "mobile-v…"), which the server never installs.</param>
public sealed record ReleaseInfo(
    string Version, string Name, string NotesUrl, bool Prerelease, DateTimeOffset PublishedAt, string? AppDownloadUrl,
    bool IsApp = false);

/// <summary>Where releases are published (GitHub Releases).</summary>
public interface IReleaseFeed
{
    /// <summary>The recent releases, newest first; empty when they cannot be read (offline, rate limited).</summary>
    Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct = default);
}

/// <summary>What the updater container last reported.</summary>
/// <param name="Heartbeat">When it last looked for work; an old one means it is not running.</param>
/// <param name="RequestId">The request this state is about; null before any.</param>
/// <param name="State">idle, backing-up, pulling, restarting, checking, done, failed or rolled-back.</param>
public sealed record UpdaterStatus(
    DateTimeOffset Heartbeat,
    string? RequestId,
    string? Version,
    string State,
    string? Message,
    DateTimeOffset? FinishedAt);

/// <summary>
/// The hand-off to the updater: the one container that may control Docker, so the API never
/// does. The API asks for a version; the updater backs up, pulls, restarts, checks and rolls back.
/// </summary>
public interface IUpdater
{
    /// <summary>The updater's last report, or null when there is no updater at all (not set up).</summary>
    Task<UpdaterStatus?> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Asks for <paramref name="version"/> to be installed; returns the request's id.</summary>
    Task<string> RequestAsync(string version, CancellationToken ct = default);
}
