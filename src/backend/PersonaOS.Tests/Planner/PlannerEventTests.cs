using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Planner;

/// <summary>
/// The planner as a calendar: events with an end, repeating events, and the three ways to change
/// one occurrence of a repeat (this event, this and following, all events), as Google Calendar
/// offers them.
/// </summary>
public class PlannerEventTests
{
    // Saturdays in October 2026: 3, 10, 17, 24, 31.
    private static readonly DateOnly Oct3 = new(2026, 10, 3);
    private static readonly DateOnly Oct10 = new(2026, 10, 10);
    private static readonly DateOnly Oct17 = new(2026, 10, 17);
    private static readonly DateOnly Oct24 = new(2026, 10, 24);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Ten = new(10, 0);

    private static (TestDbContext Db, PlannerService Planner) Setup()
    {
        var db = TestDbContext.Create();
        return (db, new PlannerService(db));
    }

    private static Task<PlannerItemDto> WeeklyAsync(PlannerService planner, string rule = "FREQ=WEEKLY") =>
        planner.CreateAsync(new CreatePlannerItemRequest(
            "Orders collection", Oct3, ScheduledTime: Nine, EndTime: Ten, NotifyBeforeMinutes: 30, Recurrence: rule));

    private static async Task<List<PlannerItemDto>> OctoberAsync(PlannerService planner) =>
        (await planner.GetRangeAsync(new DateOnly(2026, 10, 1), Oct31)).SelectMany(d => d.Items).ToList();

    // --- Events -----------------------------------------------------------------------

    [Fact]
    public async Task A_timed_event_without_an_end_lasts_an_hour()
    {
        var (_, planner) = Setup();

        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Call the bank", Oct3, ScheduledTime: new TimeOnly(16, 0)));

        Assert.Equal(new TimeOnly(17, 0), item.EndTime);
    }

    [Fact]
    public async Task An_event_late_in_the_evening_ends_by_midnight()
    {
        var (_, planner) = Setup();

        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Late call", Oct3, ScheduledTime: new TimeOnly(23, 30)));

        Assert.Equal(new TimeOnly(23, 59), item.EndTime);
    }

    [Fact]
    public async Task An_event_without_a_start_is_all_day_and_has_no_end()
    {
        var (_, planner) = Setup();

        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Gandhi Jayanti", Oct3));

        Assert.Null(item.ScheduledTime);
        Assert.Null(item.EndTime);
    }

    [Fact]
    public async Task An_end_must_follow_the_start_and_needs_one()
    {
        var (_, planner) = Setup();

        var before = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Meeting", Oct3, ScheduledTime: Ten, EndTime: Nine)));
        var alone = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Meeting", Oct3, EndTime: Ten)));

        Assert.Contains("after the start", before.Message);
        Assert.Contains("needs a start time", alone.Message);
    }

    [Fact]
    public async Task Moving_the_start_keeps_the_length()
    {
        var (_, planner) = Setup();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Send mail", Oct3, ScheduledTime: new TimeOnly(12, 0), EndTime: new TimeOnly(15, 30)));

        var moved = await planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ScheduledTime: new TimeOnly(13, 0)));

        Assert.Equal(new TimeOnly(16, 30), moved!.EndTime);
    }

    [Fact]
    public async Task Clearing_the_start_makes_it_all_day()
    {
        var (_, planner) = Setup();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Send mail", Oct3, ScheduledTime: Nine, EndTime: Ten));

        var updated = await planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ClearScheduledTime: true));

        Assert.Null(updated!.ScheduledTime);
        Assert.Null(updated.EndTime);
    }

    [Theory]
    [InlineData(-1, "before zero")]
    [InlineData(PlannerEvents.MaxNotifyBeforeMinutes + 1, "at most 4 weeks")]
    public async Task An_alert_must_be_within_four_weeks(int minutes, string reason)
    {
        var (_, planner) = Setup();

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Meeting", Oct3, ScheduledTime: Nine, NotifyBeforeMinutes: minutes)));

        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task An_alert_can_be_turned_off()
    {
        var (_, planner) = Setup();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Meeting", Oct3, ScheduledTime: Nine, NotifyBeforeMinutes: 30));

        var updated = await planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ClearNotify: true));

        Assert.Null(updated!.NotifyBeforeMinutes);
    }

    // --- Repeats ----------------------------------------------------------------------

    [Fact]
    public async Task A_weekly_event_shows_on_every_Saturday_with_which_one_it_is()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var october = await OctoberAsync(planner);

        Assert.Equal([Oct3, Oct10, Oct17, Oct24, Oct31], october.Select(i => i.Date));
        Assert.All(october, i => Assert.Equal(series.Id, i.Id));
        Assert.Equal(october.Select(i => i.Date), october.Select(i => i.OccurrenceDate!.Value));
        Assert.All(october, i => Assert.Equal("Weekly on Saturday", i.RecurrenceText));
    }

    [Fact]
    public async Task A_repeat_rule_that_cannot_be_kept_is_refused()
    {
        var (_, planner) = Setup();

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Gym", Oct3, Recurrence: "FREQ=HOURLY")));

        Assert.Contains("not supported", error.Message);
    }

    [Fact]
    public async Task A_repeat_cannot_end_before_it_starts()
    {
        var (_, planner) = Setup();

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Gym", Oct10, Recurrence: "FREQ=WEEKLY;UNTIL=20261003")));

        Assert.Contains("ends before", error.Message);
    }

    [Fact]
    public async Task Changing_one_of_a_repeat_needs_to_say_which()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.UpdateAsync(
            series.Id, new UpdatePlannerItemRequest(Title: "x"), new PlannerTarget(PlannerEditScope.This)));
        var wrongDay = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.UpdateAsync(
            series.Id, new UpdatePlannerItemRequest(Title: "x"), new PlannerTarget(PlannerEditScope.This, Oct3.AddDays(1))));

        Assert.Contains("say which occurrence", error.Message);
        Assert.Contains("does not repeat on 2026-10-04", wrongDay.Message);
    }

    // --- This event -------------------------------------------------------------------

    [Fact]
    public async Task Changing_this_event_changes_only_that_one()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var changed = await planner.UpdateAsync(
            series.Id, new UpdatePlannerItemRequest(Title: "Orders (big)", ScheduledTime: new TimeOnly(11, 0)),
            new PlannerTarget(PlannerEditScope.This, Oct17));

        Assert.Equal("Orders (big)", changed!.Title);
        Assert.Equal(new TimeOnly(11, 0), changed.ScheduledTime);
        Assert.Equal(new TimeOnly(12, 0), changed.EndTime);
        Assert.True(changed.IsChanged);

        var october = await OctoberAsync(planner);
        Assert.Equal(["Orders collection", "Orders collection", "Orders (big)", "Orders collection", "Orders collection"], october.Select(i => i.Title));
    }

    [Fact]
    public async Task Moving_this_event_to_another_day_takes_it_off_its_own_day()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var moved = await planner.MoveAsync(series.Id, Oct17.AddDays(1), new PlannerTarget(PlannerEditScope.This, Oct17));

        Assert.Equal(Oct17.AddDays(1), moved!.Date);
        Assert.Equal(Oct17, moved.OccurrenceDate);
        Assert.Equal([Oct3, Oct10, Oct17.AddDays(1), Oct24, Oct31], (await OctoberAsync(planner)).Select(i => i.Date));
    }

    [Fact]
    public async Task An_occurrence_moved_into_the_window_from_outside_it_still_shows()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        // The 31st moves to 1 November: a November view has to find it though the rule puts it in October.
        await planner.MoveAsync(series.Id, new DateOnly(2026, 11, 1), new PlannerTarget(PlannerEditScope.This, Oct31));

        var november = await planner.GetDayAsync(new DateOnly(2026, 11, 1));

        Assert.Single(november.Items);
        Assert.Equal(Oct31, november.Items[0].OccurrenceDate);
    }

    [Fact]
    public async Task Moving_a_repeat_without_a_scope_moves_only_that_day()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        await planner.MoveAsync(series.Id, Oct10.AddDays(2), new PlannerTarget(Occurrence: Oct10) with { Scope = PlannerEditScope.This });

        Assert.Equal([Oct3, Oct10.AddDays(2), Oct17, Oct24, Oct31], (await OctoberAsync(planner)).Select(i => i.Date));
    }

    [Fact]
    public async Task The_repeat_alert_and_links_cannot_differ_for_one_event()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.UpdateAsync(
            series.Id, new UpdatePlannerItemRequest(NotifyBeforeMinutes: 10), new PlannerTarget(PlannerEditScope.This, Oct17)));

        Assert.Contains("whole series", error.Message);
    }

    [Fact]
    public async Task Deleting_this_event_leaves_the_others()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "renamed"), new PlannerTarget(PlannerEditScope.This, Oct17));

        Assert.True(await planner.DeleteAsync(series.Id, new PlannerTarget(PlannerEditScope.This, Oct17)));

        Assert.Equal([Oct3, Oct10, Oct24, Oct31], (await OctoberAsync(planner)).Select(i => i.Date));
    }

    [Fact]
    public async Task Marking_one_occurrence_done_leaves_the_rest_planned()
    {
        var (db, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var done = await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Done, Oct10);
        var october = await OctoberAsync(planner);

        Assert.Equal(PlannerItemStatuses.Done, done!.Status);
        Assert.Equal(["planned", "done", "planned", "planned", "planned"], october.Select(i => i.Status));

        // Back to planned leaves nothing behind.
        await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Planned, Oct10);
        Assert.Empty(await db.PlannerOccurrences.ToListAsync());
    }

    // --- This and following -----------------------------------------------------------

    [Fact]
    public async Task Changing_this_and_following_splits_the_series_there()
    {
        var (db, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var next = await planner.UpdateAsync(
            series.Id, new UpdatePlannerItemRequest(Title: "Orders (new depot)"),
            new PlannerTarget(PlannerEditScope.Following, Oct17));

        var october = await OctoberAsync(planner);
        Assert.Equal(
            ["Orders collection", "Orders collection", "Orders (new depot)", "Orders (new depot)", "Orders (new depot)"],
            october.Select(i => i.Title));
        Assert.Equal([Oct3, Oct10, Oct17, Oct24, Oct31], october.Select(i => i.Date));
        Assert.NotEqual(series.Id, next!.Id);
        Assert.Equal(2, await db.PlannerItems.CountAsync());
        Assert.Equal("FREQ=WEEKLY;UNTIL=20261016", (await db.PlannerItems.FindAsync(series.Id))!.Recurrence);
    }

    [Fact]
    public async Task This_and_following_keeps_the_count_of_a_series_that_has_one()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner, "FREQ=WEEKLY;COUNT=4");

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "Later"), new PlannerTarget(PlannerEditScope.Following, Oct17));

        var october = await OctoberAsync(planner);
        Assert.Equal([Oct3, Oct10, Oct17, Oct24], october.Select(i => i.Date));
        Assert.Equal(["Orders collection", "Orders collection", "Later", "Later"], october.Select(i => i.Title));
    }

    [Fact]
    public async Task This_and_following_carries_later_single_changes_over()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Done, Oct31);

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Notes: "Gate 4"), new PlannerTarget(PlannerEditScope.Following, Oct17));

        var october = await OctoberAsync(planner);
        Assert.Equal(PlannerItemStatuses.Done, october.Single(i => i.Date == Oct31).Status);
        Assert.Equal("Gate 4", october.Single(i => i.Date == Oct31).Notes);
    }

    [Fact]
    public async Task This_and_following_keeps_what_the_edit_does_not_touch_on_the_day_it_starts()
    {
        // Found against SQLite: the occurrence the split starts from lost its done mark.
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Done, Oct17);
        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "Own title"), new PlannerTarget(PlannerEditScope.This, Oct17));

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "New depot"), new PlannerTarget(PlannerEditScope.Following, Oct17));

        var day = (await OctoberAsync(planner)).Single(i => i.Date == Oct17);
        Assert.Equal(PlannerItemStatuses.Done, day.Status);
        Assert.Equal("New depot", day.Title);
    }

    [Fact]
    public async Task This_and_following_to_another_day_keeps_the_done_mark_on_the_moved_start()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Done, Oct17);

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Date: Oct17.AddDays(1)), new PlannerTarget(PlannerEditScope.Following, Oct17));

        var october = await OctoberAsync(planner);
        Assert.Equal([Oct3, Oct10, Oct17.AddDays(1), Oct24.AddDays(1)], october.Select(i => i.Date));
        Assert.Equal(PlannerItemStatuses.Done, october.Single(i => i.Date == Oct17.AddDays(1)).Status);
    }

    [Fact]
    public async Task This_and_following_from_the_first_one_is_the_whole_series()
    {
        var (db, planner) = Setup();
        var series = await WeeklyAsync(planner);

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "All of them"), new PlannerTarget(PlannerEditScope.Following, Oct3));

        Assert.Equal(1, await db.PlannerItems.CountAsync());
        Assert.All(await OctoberAsync(planner), i => Assert.Equal("All of them", i.Title));
    }

    [Fact]
    public async Task Deleting_this_and_following_ends_the_series_the_day_before()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        await planner.DeleteAsync(series.Id, new PlannerTarget(PlannerEditScope.Following, Oct17));

        Assert.Equal([Oct3, Oct10], (await OctoberAsync(planner)).Select(i => i.Date));
    }

    // --- All events -------------------------------------------------------------------

    [Fact]
    public async Task Changing_all_events_changes_every_one()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(ScheduledTime: new TimeOnly(8, 30)), new PlannerTarget(PlannerEditScope.All, Oct17));

        var october = await OctoberAsync(planner);
        Assert.All(october, i => Assert.Equal(new TimeOnly(8, 30), i.ScheduledTime));
        Assert.All(october, i => Assert.Equal(new TimeOnly(9, 30), i.EndTime));
    }

    [Fact]
    public async Task Moving_all_events_from_one_of_them_moves_the_series_by_as_much()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        // Moving the 17th to Monday the 19th moves every Saturday to a Monday.
        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Date: Oct17.AddDays(2)), new PlannerTarget(PlannerEditScope.All, Oct17));

        var october = await OctoberAsync(planner);
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26)], october.Select(i => i.Date));
        Assert.All(october, i => Assert.Equal("Weekly on Monday", i.RecurrenceText));
    }

    [Fact]
    public async Task Ending_the_repeat_keeps_the_occurrence_that_was_open()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);

        var single = await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(ClearRecurrence: true), new PlannerTarget(PlannerEditScope.All, Oct17));

        Assert.Null(single!.Recurrence);
        Assert.Equal([Oct17], (await OctoberAsync(planner)).Select(i => i.Date));
    }

    [Fact]
    public async Task Deleting_all_events_removes_the_series_and_its_changes()
    {
        var (db, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateStatusAsync(series.Id, PlannerItemStatuses.Done, Oct10);

        Assert.True(await planner.DeleteAsync(series.Id));

        Assert.Empty(await OctoberAsync(planner));
        Assert.Empty(await db.PlannerOccurrences.ToListAsync());
    }

    [Fact]
    public async Task One_occurrence_can_be_read_back_by_its_day()
    {
        var (_, planner) = Setup();
        var series = await WeeklyAsync(planner);
        await planner.UpdateAsync(series.Id, new UpdatePlannerItemRequest(Title: "Moved and renamed", Date: Oct24.AddDays(1)), new PlannerTarget(PlannerEditScope.This, Oct24));

        var item = await planner.GetAsync(series.Id, Oct24);

        Assert.Equal("Moved and renamed", item!.Title);
        Assert.Equal(Oct24.AddDays(1), item.Date);
    }
}
