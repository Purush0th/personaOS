using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Application.Updates;

/// <summary>The version this server runs, from the build (the release tag), set once at startup.</summary>
public sealed record RunningVersion(string Value);

/// <summary>Where updates stand, for Settings → Updates and the banner.</summary>
/// <param name="Latest">The newest release this server follows, or null when none could be read.</param>
/// <param name="CanInstall">An updater is running, so Update now can work.</param>
public sealed record UpdatesView(
    string CurrentVersion,
    ReleaseInfo? Latest,
    bool UpdateAvailable,
    bool AutoInstall,
    bool CanInstall,
    UpdaterStatus? Updater);

public interface IUpdateService
{
    Task<UpdatesView> GetAsync(CancellationToken ct = default);

    /// <summary>Asks the updater to install <paramref name="version"/>; refuses one that is not a newer release.</summary>
    Task<UpdatesView> InstallAsync(string version, CancellationToken ct = default);

    Task<UpdatesView> SetAutoInstallAsync(bool enabled, CancellationToken ct = default);

    /// <summary>With automatic install on, asks for the newest release when there is one; the background check calls this.</summary>
    Task<string?> InstallIfDueAsync(CancellationToken ct = default);
}

/// <summary>Thrown when an install is refused; the message is for the user.</summary>
public sealed class UpdateRefusedException(string message) : Exception(message);

/// <summary>
/// Updates as a feature of PersonaOS (owner, 2026-10-24: "think of how our user will receive the
/// update"): the server knows its version, reads the releases, and an Update now button or the
/// automatic install asks the updater container to apply one. A server on a prerelease follows
/// prereleases; one on a release only takes releases.
/// </summary>
public sealed class UpdateService(
    RunningVersion running,
    IReleaseFeed feed,
    IUpdater updater,
    IInstanceConfigService configService,
    TimeProvider time,
    ILogger<UpdateService> logger) : IUpdateService
{
    /// <summary>An updater that has not looked for work in this long is taken as not running.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] Working = ["requested", "backing-up", "pulling", "restarting", "checking"];

    public async Task<UpdatesView> GetAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var current = ReleaseVersion.Parse(running.Value);
        var latest = Newest(await feed.GetReleasesAsync(ct), current);
        var status = await updater.GetStatusAsync(ct);
        // Running and able to: one that cannot find the compose folder says "not-set-up".
        var alive = status is not null && time.GetUtcNow() - status.Heartbeat < HeartbeatTimeout && status.State != "not-set-up";

        return new UpdatesView(
            running.Value,
            latest,
            latest is not null && current is not null && ReleaseVersion.Parse(latest.Version)!.IsNewerThan(current),
            config.AutoInstallUpdates,
            alive,
            status);
    }

    public async Task<UpdatesView> InstallAsync(string version, CancellationToken ct = default)
    {
        var view = await GetAsync(ct);
        if (!view.CanInstall)
            throw new UpdateRefusedException("The updater is not running on this server, so it cannot update itself. See README, \"Updates\".");
        if (view.Updater is { State: var state } && Working.Contains(state))
            throw new UpdateRefusedException("An update is already in progress.");

        var wanted = ReleaseVersion.Parse(version);
        var release = (await feed.GetReleasesAsync(ct)).FirstOrDefault(r => ReleaseVersion.Parse(r.Version) is { } v && v.Equals(wanted));
        if (wanted is null || release is null)
            throw new UpdateRefusedException($"There is no release {version}.");
        if (ReleaseVersion.Parse(view.CurrentVersion) is { } current && !wanted.IsNewerThan(current))
            throw new UpdateRefusedException($"This server already runs {view.CurrentVersion}.");

        var id = await updater.RequestAsync(release.Version, ct);
        logger.LogInformation("Asked the updater for {Version} (request {Request}).", release.Version, id);
        return await GetAsync(ct);
    }

    public async Task<UpdatesView> SetAutoInstallAsync(bool enabled, CancellationToken ct = default)
    {
        await configService.UpdateAsync(c => c.AutoInstallUpdates = enabled, ct);
        return await GetAsync(ct);
    }

    public async Task<string?> InstallIfDueAsync(CancellationToken ct = default)
    {
        var view = await GetAsync(ct);
        if (!view.AutoInstall || !view.CanInstall || !view.UpdateAvailable || view.Latest is null) return null;

        // A version that failed and was rolled back is not tried again on its own: the user decides.
        if (view.Updater is { State: "failed" or "rolled-back" } last && last.Version == view.Latest.Version) return null;
        if (view.Updater is { State: var state } && Working.Contains(state)) return null;

        await InstallAsync(view.Latest.Version, ct);
        return view.Latest.Version;
    }

    /// <summary>The newest release this server follows: prereleases only when it runs one.</summary>
    private static ReleaseInfo? Newest(IReadOnlyList<ReleaseInfo> releases, ReleaseVersion? current)
    {
        var followsPrereleases = current is null || current.IsPrerelease;
        return releases
            .Select(r => (Release: r, Version: ReleaseVersion.Parse(r.Version)))
            .Where(r => r.Version is not null && (followsPrereleases || !r.Version.IsPrerelease))
            .OrderByDescending(r => r.Version)
            .Select(r => r.Release)
            .FirstOrDefault();
    }
}
