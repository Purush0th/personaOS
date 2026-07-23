using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Proactive;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Proactive;

/// <summary>
/// The scheduler fires unattended, so the rules that matter most are: never send
/// the same brief twice, never fire a stale one after downtime, and stay silent
/// when there is nothing to say.
/// </summary>
public class ProactiveServiceTests
{
    /// <summary>Frozen "today" at 09:00 UTC, so scheduled times are unambiguous.</summary>
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);
    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    /// <summary>A time already passed today (within the late-run threshold).</summary>
    private static readonly TimeOnly JustPassed = new(8, 30);

    /// <summary>A time still ahead today.</summary>
    private static readonly TimeOnly NotYet = new(22, 0);

    private static (TestDbContext Db, FakePushSender Push, ProactiveService Service, FakeInstanceConfigService Config)
        Setup(bool proactiveEnabled = true, bool morningEnabled = true, TimeOnly? morning = null, TimeOnly? evening = null)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);

        config.UpdateAsync(c =>
        {
            c.Features[InstanceConfig.Modules.Proactive] = proactiveEnabled;
            // morningEnabled distinguishes "use the default time" from "disabled".
            c.MorningBriefTime = morningEnabled ? morning ?? JustPassed : null;
            c.EveningRollupTime = evening;
        }).GetAwaiter().GetResult();

        var push = new FakePushSender();
        var service = new ProactiveService(
            db, config, new ProactiveBriefComposer(db), push,
            new FixedTimeProvider(Now), NullLogger<ProactiveService>.Instance);
        return (db, push, service, config);
    }

    private static async Task AddPlannedItemAsync(TestDbContext db, DateOnly date, string title = "Write the docs")
    {
        db.PlannerItems.Add(new PlannerItem { Title = title, Date = date, Status = PlannerItemStatuses.Planned });
        db.DeviceTokens.Add(new DeviceToken { Token = $"tok-{Guid.NewGuid():N}", Platform = DevicePlatforms.Android });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Morning_brief_runs_pushes_and_files_a_chat_entry()
    {
        var (db, push, service, _) = Setup();
        await AddPlannedItemAsync(db, Today, "Ship Phase 8");

        var results = await service.RunDueJobsAsync();

        var brief = Assert.Single(results, r => r.JobName == ProactiveJobs.MorningBrief);
        Assert.True(brief.Ran);
        Assert.True(brief.Pushed);
        Assert.Contains("Ship Phase 8", brief.Summary);
        Assert.Equal(1, push.SendCount);

        // Filed into chat so the user can reply to it with context.
        var message = await db.ChatMessages.SingleAsync();
        Assert.Equal(ChatRoles.Assistant, message.Role);
        Assert.Contains("Ship Phase 8", message.Content);
    }

    [Fact]
    public async Task Second_pass_on_the_same_day_does_not_resend()
    {
        var (db, push, service, _) = Setup();
        await AddPlannedItemAsync(db, Today);

        await service.RunDueJobsAsync();
        var second = await service.RunDueJobsAsync();

        Assert.Empty(second);
        Assert.Equal(1, push.SendCount);
        Assert.Equal(1, await db.ProactiveJobRuns.CountAsync());
    }

    [Fact]
    public async Task A_job_whose_time_has_not_arrived_does_not_run()
    {
        var (db, push, service, _) = Setup(morning: NotYet);
        await AddPlannedItemAsync(db, Today);

        var results = await service.RunDueJobsAsync();

        Assert.Empty(results);
        Assert.Equal(0, push.SendCount);
    }

    [Fact]
    public async Task Nothing_to_report_records_the_run_but_sends_nothing()
    {
        var (db, push, service, _) = Setup();
        db.DeviceTokens.Add(new DeviceToken { Token = "tok", Platform = DevicePlatforms.Android });
        await db.SaveChangesAsync();

        var results = await service.RunDueJobsAsync();

        var brief = Assert.Single(results);
        Assert.True(brief.Ran);
        Assert.Null(brief.Summary);
        Assert.Equal("nothing to report", brief.Skipped);
        Assert.Equal(0, push.SendCount);
        Assert.Empty(await db.ChatMessages.ToListAsync());
        // Recorded, so it won't be retried every tick for the rest of the day.
        Assert.Equal(1, await db.ProactiveJobRuns.CountAsync());
    }

    [Fact]
    public async Task Disabled_proactive_feature_runs_nothing()
    {
        var (db, push, service, _) = Setup(proactiveEnabled: false);
        await AddPlannedItemAsync(db, Today);

        Assert.Empty(await service.RunDueJobsAsync());
        Assert.Equal(0, push.SendCount);
    }

    [Fact]
    public async Task A_null_scheduled_time_disables_just_that_job()
    {
        var (db, push, service, config) = Setup();
        await config.UpdateAsync(c => c.MorningBriefTime = null);
        await AddPlannedItemAsync(db, Today);

        Assert.Empty(await service.RunDueJobsAsync());
        Assert.Equal(0, push.SendCount);
    }

    [Fact]
    public async Task Manual_run_works_even_when_the_schedule_has_not_fired()
    {
        var (db, push, service, _) = Setup(morning: NotYet);
        await AddPlannedItemAsync(db, Today, "Manual check");

        var result = await service.RunJobAsync(ProactiveJobs.MorningBrief);

        Assert.True(result.Ran);
        Assert.Contains("Manual check", result.Summary);
        Assert.Equal(1, push.SendCount);
    }

    [Fact]
    public async Task Manual_run_without_force_respects_the_once_per_day_rule()
    {
        var (db, _, service, _) = Setup();
        await AddPlannedItemAsync(db, Today);

        await service.RunJobAsync(ProactiveJobs.MorningBrief);
        var second = await service.RunJobAsync(ProactiveJobs.MorningBrief, force: false);

        Assert.False(second.Ran);
        Assert.Equal("already ran today", second.Skipped);
    }

    [Fact]
    public async Task Unknown_job_name_is_rejected()
    {
        var (_, _, service, _) = Setup();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunJobAsync("not_a_job"));
    }

    [Fact]
    public async Task Delivery_still_records_the_run_when_push_is_unconfigured()
    {
        var (db, push, service, _) = Setup();
        push.IsConfigured = false;
        await AddPlannedItemAsync(db, Today);

        var result = Assert.Single(await service.RunDueJobsAsync());

        Assert.True(result.Ran);
        Assert.False(result.Pushed);
        // The brief is still readable in chat even with no push provider.
        Assert.NotNull(result.Summary);
        Assert.Single(await db.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task Evening_rollup_summarises_done_and_open_items()
    {
        var (db, _, service, _) = Setup(morningEnabled: false, evening: JustPassed);
        db.PlannerItems.AddRange(
            new PlannerItem { Title = "Done thing", Date = Today, Status = PlannerItemStatuses.Done },
            new PlannerItem { Title = "Open thing", Date = Today, Status = PlannerItemStatuses.Planned });
        await db.SaveChangesAsync();

        var result = Assert.Single(await service.RunDueJobsAsync());

        Assert.Equal(ProactiveJobs.EveningRollup, result.JobName);
        Assert.Contains("1 of 2 done", result.Summary);
        Assert.Contains("Open thing", result.Summary);
        Assert.Contains("move these to tomorrow", result.Summary);
    }
}
