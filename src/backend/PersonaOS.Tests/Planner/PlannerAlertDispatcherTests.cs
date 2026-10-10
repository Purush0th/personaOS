using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Planner;

/// <summary>
/// The heads-up before a planner event: an ordinary notification, sent once, in the window between
/// the alert time and the start.
/// </summary>
public class PlannerAlertDispatcherTests
{
    private static readonly DateOnly Sat3Oct = new(2026, 10, 3);

    private sealed record Rig(
        TestDbContext Db, PlannerService Planner, FakePushSender Push, FixedTimeProvider Clock, PlannerAlertDispatcher Dispatcher);

    private static async Task<Rig> SetupAsync(string timeZone = "UTC", bool withDevice = true)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        (await config.GetOrCreateAsync()).TimeZone = timeZone;
        if (withDevice) db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = DevicePlatforms.Android });
        await db.SaveChangesAsync();

        var push = new FakePushSender();
        var clock = new FixedTimeProvider(At(8, 0));
        var dispatcher = new PlannerAlertDispatcher(db, push, config, clock, NullLogger<PlannerAlertDispatcher>.Instance);
        return new Rig(db, new PlannerService(db), push, clock, dispatcher);
    }

    private static DateTimeOffset At(int hour, int minute, DateOnly? day = null) =>
        new((day ?? Sat3Oct).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static Task<PlannerItemDto> TenOClockAsync(PlannerService planner, int? notify = 30, string? recurrence = null) =>
        planner.CreateAsync(new CreatePlannerItemRequest(
            "Customer calls", Sat3Oct, ScheduledTime: new TimeOnly(10, 0), NotifyBeforeMinutes: notify, Recurrence: recurrence));

    [Fact]
    public async Task The_notification_goes_out_at_the_alert_time_once()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(9, 29);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        rig.Clock.Now = At(9, 30);
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal("Customer calls", rig.Push.SentTitles.Single());
        Assert.Equal("Starts in 30 minutes · 10:00 am", rig.Push.SentBodies.Single());
        Assert.Equal("planner_alert", rig.Push.SentNotificationData.Single()!["type"]);

        rig.Clock.Now = At(9, 45);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal(1, rig.Push.SendCount);
    }

    [Fact]
    public async Task An_event_that_has_started_gets_only_the_start_notification()
    {
        // The server was down over the alert time and came back after the start.
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(10, 1);

        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal("Starts now · 10:00 am", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task When_it_starts_a_second_notification_comes_beside_the_first()
    {
        // Owner, 2026-10-10: Google Calendar also notifies when the event starts.
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(9, 30);
        await rig.Dispatcher.DispatchDueAsync();
        rig.Clock.Now = At(9, 59);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
        rig.Clock.Now = At(10, 0);
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        rig.Clock.Now = At(10, 3);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        Assert.Equal(["Starts in 30 minutes · 10:00 am", "Starts now · 10:00 am"], rig.Push.SentBodies);
        // Owner: keep both on the phone, so they are tagged apart.
        Assert.Equal(2, rig.Push.SentTags.Distinct().Count());
        Assert.Equal(rig.Push.SentTags, rig.Push.SentNotificationData.Select(d => d!["tag"]));
    }

    [Fact]
    public async Task A_start_missed_by_more_than_five_minutes_is_not_announced()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(10, 6);

        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task An_all_day_event_starts_at_midnight_and_its_heads_up_counts_back_from_then()
    {
        // Owner, 2026-10-10: "All day event also can notify at 12AM".
        var rig = await SetupAsync();
        await rig.Planner.CreateAsync(new CreatePlannerItemRequest("Durga Puja", Sat3Oct, NotifyBeforeMinutes: 60));

        rig.Clock.Now = At(22, 59, Sat3Oct.AddDays(-1));
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
        rig.Clock.Now = At(23, 0, Sat3Oct.AddDays(-1));
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        rig.Clock.Now = At(0, 0);
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        rig.Clock.Now = At(9, 0);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        Assert.Equal(["All day tomorrow", "All day today"], rig.Push.SentBodies);
    }

    [Fact]
    public async Task A_late_tick_still_sends_before_the_start()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(9, 50);

        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task An_alert_at_the_start_is_sent_at_the_start()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner, notify: 0);

        rig.Clock.Now = At(10, 2);

        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal("Starts now · 10:00 am", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task No_heads_up_means_only_the_start_notification()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner, notify: null);

        rig.Clock.Now = At(9, 59);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        // Every event is still notified when it starts (owner, 2026-10-10).
        rig.Clock.Now = At(10, 0);
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal("Starts now · 10:00 am", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task A_done_event_is_not_announced()
    {
        var rig = await SetupAsync();
        var item = await TenOClockAsync(rig.Planner);
        await rig.Planner.UpdateStatusAsync(item.Id, PlannerItemStatuses.Done);

        rig.Clock.Now = At(9, 40);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        rig.Clock.Now = At(10, 0);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task The_start_is_the_users_local_time()
    {
        // 10:00 in India is 04:30 UTC, so the 30-minute heads-up is due at 04:00 UTC.
        var rig = await SetupAsync("Asia/Kolkata");
        await TenOClockAsync(rig.Planner);

        rig.Clock.Now = At(3, 59);
        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        rig.Clock.Now = At(4, 0);
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task A_lead_of_a_day_reaches_back_into_the_day_before()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner, notify: 24 * 60);

        rig.Clock.Now = At(10, 0, Sat3Oct.AddDays(-1));

        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal("Starts in 1 day · 10:00 am", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task Each_occurrence_of_a_repeat_is_announced_with_its_day()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner, recurrence: "FREQ=WEEKLY");
        var nextWeek = Sat3Oct.AddDays(7);

        rig.Clock.Now = At(9, 35);
        await rig.Dispatcher.DispatchDueAsync();
        rig.Clock.Now = At(9, 35, nextWeek);
        await rig.Dispatcher.DispatchDueAsync();

        Assert.Equal(["2026-10-03", "2026-10-10"], rig.Push.SentNotificationData.Select(d => d!["occurrenceDate"]));
    }

    [Fact]
    public async Task Moving_an_event_after_its_alert_was_sent_announces_it_again()
    {
        var rig = await SetupAsync();
        var item = await TenOClockAsync(rig.Planner);
        rig.Clock.Now = At(9, 35);
        await rig.Dispatcher.DispatchDueAsync();

        await rig.Planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ScheduledTime: new TimeOnly(11, 0)));
        rig.Clock.Now = At(10, 35);

        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
        Assert.Equal(2, rig.Push.SendCount);
    }

    [Fact]
    public async Task Without_push_set_up_nothing_is_marked_sent_so_it_goes_out_once_it_is()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);
        rig.Push.IsConfigured = false;
        rig.Clock.Now = At(9, 35);

        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());
        Assert.Empty(await rig.Db.PlannerAlertLogs.ToListAsync());

        rig.Push.IsConfigured = true;
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task A_failed_send_is_tried_again_on_the_next_pass()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);
        rig.Clock.Now = At(9, 35);
        rig.Push.Succeeds = false;

        Assert.Equal(0, await rig.Dispatcher.DispatchDueAsync());

        rig.Push.Succeeds = true;
        Assert.Equal(1, await rig.Dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task Records_of_sent_alerts_are_dropped_after_a_week()
    {
        var rig = await SetupAsync();
        await TenOClockAsync(rig.Planner);
        rig.Clock.Now = At(9, 35);
        await rig.Dispatcher.DispatchDueAsync();

        rig.Clock.Now = At(9, 36, Sat3Oct.AddDays(8));
        await rig.Dispatcher.DispatchDueAsync();

        Assert.Empty(await rig.Db.PlannerAlertLogs.ToListAsync());
    }

    [Theory]
    [InlineData(5, "5 minutes")]
    [InlineData(1, "1 minute")]
    [InlineData(25, "25 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "90 minutes")]
    [InlineData(480, "8 hours")]
    [InlineData(1440, "1 day")]
    [InlineData(2880, "2 days")]
    [InlineData(10080, "1 week")]
    public void The_lead_is_said_in_the_largest_whole_unit(int minutes, string expected)
    {
        Assert.Equal(expected, PlannerAlertDispatcher.Lead(minutes));
    }
}
