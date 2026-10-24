using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Updates;
using PersonaOS.Infrastructure.Updates;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Updates;

/// <summary>
/// Updates as a feature (owner, 2026-10-24): the server knows its version, reads the releases,
/// and asks the updater container for a newer one, from Update now or by itself.
/// </summary>
public class UpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero);

    private static readonly UpdaterStatus Alive = new(Now.AddSeconds(-10), null, null, "idle", null, null);

    private static (UpdateService Service, FakeReleaseFeed Feed, FakeUpdater Updater) Setup(
        string running, UpdaterStatus? status, params ReleaseInfo[] releases)
    {
        var db = TestDbContext.Create();
        var feed = new FakeReleaseFeed(releases);
        var updater = new FakeUpdater { Status = status };
        var service = new UpdateService(new RunningVersion(running), feed, updater, new FakeInstanceConfigService(db),
            new FixedTimeProvider(Now), NullLogger<UpdateService>.Instance);
        return (service, feed, updater);
    }

    [Theory]
    [InlineData("0.1.0-alpha.10", "0.1.0-alpha.9")]
    [InlineData("0.1.0-beta", "0.1.0-alpha.12")]
    [InlineData("0.1.0", "0.1.0-rc.1")]
    [InlineData("0.2.0-alpha.1", "0.1.9")]
    [InlineData("v1.0.0", "0.9.9")]
    [InlineData("0.1.0-alpha.1", "0.0.0-dev")]
    public void Versions_are_ordered_as_releases_are(string newer, string older)
    {
        Assert.True(ReleaseVersion.Parse(newer)!.IsNewerThan(ReleaseVersion.Parse(older)!));
        Assert.False(ReleaseVersion.Parse(older)!.IsNewerThan(ReleaseVersion.Parse(newer)!));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("")]
    public void Anything_but_a_version_is_not_one(string text) => Assert.Null(ReleaseVersion.Parse(text));

    [Fact]
    public async Task A_server_on_a_prerelease_follows_prereleases()
    {
        var (service, _, _) = Setup("0.1.0-alpha.8", Alive,
            FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true), FakeReleaseFeed.Release("0.1.0-alpha.10", prerelease: true));

        var view = await service.GetAsync();

        Assert.Equal("0.1.0-alpha.10", view.Latest!.Version);
        Assert.True(view.UpdateAvailable);
        Assert.True(view.CanInstall);
    }

    [Fact]
    public async Task A_server_on_a_release_takes_only_releases()
    {
        var (service, _, _) = Setup("1.0.0", Alive,
            FakeReleaseFeed.Release("1.1.0-beta.1", prerelease: true), FakeReleaseFeed.Release("1.0.0"));

        var view = await service.GetAsync();

        Assert.Equal("1.0.0", view.Latest!.Version);
        Assert.False(view.UpdateAvailable);
    }

    [Fact]
    public async Task Update_now_asks_the_updater_for_the_release()
    {
        var (service, _, updater) = Setup("0.1.0-alpha.8", Alive, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));

        var view = await service.InstallAsync("0.1.0-alpha.9");

        Assert.Equal(["0.1.0-alpha.9"], updater.Requested);
        Assert.Equal("requested", view.Updater!.State);
    }

    [Fact]
    public async Task Only_a_newer_published_release_can_be_installed()
    {
        var (service, _, updater) = Setup("0.1.0-alpha.9", Alive, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));

        await Assert.ThrowsAsync<UpdateRefusedException>(() => service.InstallAsync("0.1.0-alpha.9"));
        await Assert.ThrowsAsync<UpdateRefusedException>(() => service.InstallAsync("9.9.9"));
        await Assert.ThrowsAsync<UpdateRefusedException>(() => service.InstallAsync("latest; rm -rf /"));
        Assert.Empty(updater.Requested);
    }

    [Fact]
    public async Task Without_a_running_updater_nothing_is_asked_for()
    {
        var stale = Alive with { Heartbeat = Now - UpdateService.HeartbeatTimeout - TimeSpan.FromSeconds(1) };
        var (service, _, updater) = Setup("0.1.0-alpha.8", stale, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));

        Assert.False((await service.GetAsync()).CanInstall);
        await Assert.ThrowsAsync<UpdateRefusedException>(() => service.InstallAsync("0.1.0-alpha.9"));
        Assert.Empty(updater.Requested);
    }

    [Fact]
    public async Task Automatic_install_takes_a_new_release_once()
    {
        var (service, _, updater) = Setup("0.1.0-alpha.8", Alive, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));
        await service.SetAutoInstallAsync(true);

        Assert.Equal("0.1.0-alpha.9", await service.InstallIfDueAsync());
        // Already on its way: not asked for a second time.
        Assert.Null(await service.InstallIfDueAsync());
        Assert.Single(updater.Requested);
    }

    [Fact]
    public async Task Automatic_install_does_not_retry_a_release_that_was_rolled_back()
    {
        var rolledBack = Alive with { RequestId = "r1", Version = "0.1.0-alpha.9", State = "rolled-back" };
        var (service, _, updater) = Setup("0.1.0-alpha.8", rolledBack, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));
        await service.SetAutoInstallAsync(true);

        Assert.Null(await service.InstallIfDueAsync());
        Assert.Empty(updater.Requested);
    }

    [Fact]
    public async Task Automatic_install_is_off_unless_turned_on()
    {
        var (service, _, updater) = Setup("0.1.0-alpha.8", Alive, FakeReleaseFeed.Release("0.1.0-alpha.9", prerelease: true));

        Assert.Null(await service.InstallIfDueAsync());
        Assert.Empty(updater.Requested);
    }

    [Fact]
    public void Releases_are_read_from_the_github_list_without_drafts()
    {
        using var doc = JsonDocument.Parse("""
            [
              {"tag_name":"v0.1.0-alpha.10","name":"Alpha 10","html_url":"https://github.com/o/r/releases/tag/v0.1.0-alpha.10",
               "prerelease":true,"draft":false,"published_at":"2026-10-24T05:00:00Z",
               "assets":[{"name":"docker-compose.yml","browser_download_url":"https://x/compose"},
                         {"name":"personaos-v0.1.0-alpha.10.apk","browser_download_url":"https://x/app.apk"}]},
              {"tag_name":"v0.2.0","draft":true}
            ]
            """);

        var release = Assert.Single(GitHubReleaseFeed.Parse(doc.RootElement));

        Assert.Equal("0.1.0-alpha.10", release.Version);
        Assert.True(release.Prerelease);
        Assert.Equal("https://x/app.apk", release.AppDownloadUrl);
    }

    [Fact]
    public async Task The_updater_folder_carries_the_request_and_reports_it_until_picked_up()
    {
        var folder = Directory.CreateTempSubdirectory("updates-").FullName;
        try
        {
            var updater = new FileUpdater(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Updates:Path"] = folder }).Build(),
                new FixedTimeProvider(Now));
            await File.WriteAllTextAsync(Path.Combine(folder, "status.json"),
                """{"heartbeat":"2026-10-24T05:59:50Z","state":"idle"}""");

            var id = await updater.RequestAsync("0.1.0-alpha.9");

            using (var request = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "request.json"))))
            {
                Assert.Equal(id, request.RootElement.GetProperty("id").GetString());
                Assert.Equal("0.1.0-alpha.9", request.RootElement.GetProperty("version").GetString());
            }
            var status = await updater.GetStatusAsync();
            Assert.Equal("requested", status!.State);
            Assert.Equal(Now.AddSeconds(-10), status.Heartbeat);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Without_an_updater_folder_there_is_no_updater()
    {
        var updater = new FileUpdater(new ConfigurationBuilder().Build(), new FixedTimeProvider(Now));

        Assert.Null(await updater.GetStatusAsync());
    }
}
