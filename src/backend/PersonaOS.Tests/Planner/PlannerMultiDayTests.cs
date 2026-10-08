using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Planner;

/// <summary>
/// Events over several days (a trip, a conference): listed once, read on each of their days, moved
/// whole, and repeated with every occurrence as long as the first.
/// </summary>
public class PlannerMultiDayTests
{
    // October 2026: Friday 9, Saturday 10, Sunday 11; Fridays are 2, 9, 16, 23, 30.
    private static readonly DateOnly Oct2 = new(2026, 10, 2);
    private static readonly DateOnly Oct9 = new(2026, 10, 9);
    private static readonly DateOnly Oct10 = new(2026, 10, 10);
    private static readonly DateOnly Oct11 = new(2026, 10, 11);
    private static readonly DateOnly Oct12 = new(2026, 10, 12);
    private static readonly DateOnly Oct16 = new(2026, 10, 16);
    private static readonly DateOnly Oct18 = new(2026, 10, 18);
    private static readonly DateOnly Oct23 = new(2026, 10, 23);
    private static readonly DateOnly Oct25 = new(2026, 10, 25);

    private static (TestDbContext Db, PlannerService Planner) Setup()
    {
        var db = TestDbContext.Create();
        return (db, new PlannerService(db));
    }

    private static Task<PlannerItemDto> TripAsync(PlannerService planner, string? recurrence = null) =>
        planner.CreateAsync(new CreatePlannerItemRequest("Goa trip", Oct9, EndDate: Oct11, Recurrence: recurrence));

    private static async Task<List<PlannerItemDto>> RangeAsync(PlannerService planner, DateOnly from, DateOnly to) =>
        (await planner.GetRangeAsync(from, to)).SelectMany(d => d.Items).ToList();

    [Fact]
    public async Task An_event_over_several_days_is_listed_once_and_found_on_each_day()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner);

        Assert.Equal(Oct11, trip.EndDate);
        var week = await planner.GetRangeAsync(new DateOnly(2026, 10, 5), Oct11);
        Assert.Equal(Oct9, Assert.Single(week).Date);
        Assert.Single(week.Single().Items);

        Assert.Equal("Goa trip", Assert.Single((await planner.GetDayAsync(Oct10)).Items).Title);
        Assert.Empty((await planner.GetDayAsync(Oct12)).Items);

        // Begun before the range, it is listed on the range's first day.
        var later = await planner.GetRangeAsync(Oct10, Oct12);
        Assert.Equal(Oct10, Assert.Single(later).Date);
        Assert.Equal(Oct9, later.Single().Items.Single().Date);
    }

    [Fact]
    public async Task A_last_day_that_is_the_first_is_a_one_day_event()
    {
        var (db, planner) = Setup();

        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Dentist", Oct9, EndDate: Oct9));

        Assert.Null((await db.PlannerItems.SingleAsync()).EndDate);
        Assert.Equal(Oct9, item.EndDate);
    }

    [Fact]
    public async Task The_last_day_cannot_be_before_the_first()
    {
        var (_, planner) = Setup();

        var error = await Assert.ThrowsAsync<PlannerValidationException>(
            () => planner.CreateAsync(new CreatePlannerItemRequest("Trip", Oct9, EndDate: Oct2)));

        Assert.Contains("on or after the start day", error.Message);
    }

    [Fact]
    public async Task A_timed_event_over_several_days_needs_an_end_which_may_be_earlier_in_the_day()
    {
        var (_, planner) = Setup();

        var missing = await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(
            new CreatePlannerItemRequest("Conference", Oct9, ScheduledTime: new TimeOnly(18, 0), EndDate: Oct11)));
        Assert.Contains("needs an end time", missing.Message);

        var item = await planner.CreateAsync(new CreatePlannerItemRequest(
            "Conference", Oct9, ScheduledTime: new TimeOnly(18, 0), EndTime: new TimeOnly(9, 0), EndDate: Oct11));
        Assert.Equal(new TimeOnly(9, 0), item.EndTime);
    }

    [Fact]
    public async Task Moving_it_keeps_how_many_days_it_lasts()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner);

        var moved = await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(Date: Oct16));
        Assert.Equal(Oct18, moved!.EndDate);

        var movedAgain = await planner.MoveAsync(trip.Id, Oct23);
        Assert.Equal(Oct25, movedAgain!.EndDate);
    }

    [Fact]
    public async Task A_new_last_day_makes_it_longer_or_back_to_one_day()
    {
        var (db, planner) = Setup();
        var trip = await TripAsync(planner);

        Assert.Equal(Oct12, (await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: Oct12)))!.EndDate);
        Assert.Equal(Oct9, (await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: Oct9)))!.EndDate);
        Assert.Null((await db.PlannerItems.SingleAsync()).EndDate);
    }

    [Fact]
    public async Task Moving_the_start_time_of_one_over_several_days_leaves_its_end()
    {
        var (_, planner) = Setup();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest(
            "Conference", Oct9, ScheduledTime: new TimeOnly(18, 0), EndTime: new TimeOnly(9, 0), EndDate: Oct11));

        var moved = await planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ScheduledTime: new TimeOnly(20, 0)));

        Assert.Equal(new TimeOnly(9, 0), moved!.EndTime);
    }

    [Fact]
    public async Task A_weekly_weekend_shows_the_one_that_began_before_the_range()
    {
        var (_, planner) = Setup();
        await TripAsync(planner, "FREQ=WEEKLY");

        var sunday = Assert.Single(await RangeAsync(planner, Oct18, Oct18));

        Assert.Equal(Oct16, sunday.Date);
        Assert.Equal(Oct16, sunday.OccurrenceDate);
        Assert.Equal(Oct18, sunday.EndDate);
    }

    [Fact]
    public async Task One_weekend_made_longer_leaves_the_others_and_keeps_its_length_when_moved()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner, "FREQ=WEEKLY");
        var thisOne = new PlannerTarget(PlannerEditScope.This, Oct16);

        await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: new DateOnly(2026, 10, 19)), thisOne);
        var moved = await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(Date: Oct23), thisOne);

        Assert.Equal(new DateOnly(2026, 10, 26), moved!.EndDate);
        var october = await RangeAsync(planner, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal([Oct11, new DateOnly(2026, 10, 26), Oct25, new DateOnly(2026, 11, 1)],
            october.Where(o => o.Date >= Oct9).Select(o => o.EndDate!.Value));
    }

    [Fact]
    public async Task This_and_following_with_a_new_last_day_lengthens_the_rest()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner, "FREQ=WEEKLY");

        await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: Oct12, Date: Oct9),
            new PlannerTarget(PlannerEditScope.Following, Oct16));
        var fridays = (await RangeAsync(planner, Oct9, new DateOnly(2026, 10, 31))).Where(o => o.Date.DayOfWeek == DayOfWeek.Friday);

        // The split moved the following ones to start a week earlier, 9th, and from there they last four days.
        Assert.Equal([Oct11, Oct12, new DateOnly(2026, 10, 19)], fridays.Select(o => o.EndDate!.Value).Take(3));
    }

    [Fact]
    public async Task All_events_with_a_new_last_day_counts_it_from_the_one_being_looked_at()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner, "FREQ=WEEKLY");

        await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: new DateOnly(2026, 10, 17)),
            new PlannerTarget(PlannerEditScope.All, Oct16));

        var first = (await RangeAsync(planner, Oct9, Oct9)).Single();
        Assert.Equal(Oct10, first.EndDate);
    }

    [Fact]
    public async Task Dragging_an_occurrence_moved_on_its_own_moves_all_events_by_what_was_seen()
    {
        var (_, planner) = Setup();
        var weekly = await planner.CreateAsync(new CreatePlannerItemRequest("Orders", Oct9, Recurrence: "FREQ=WEEKLY"));
        await planner.UpdateAsync(weekly.Id, new UpdatePlannerItemRequest(Date: new DateOnly(2026, 10, 17)), new PlannerTarget(PlannerEditScope.This, Oct16));

        // Seen on Saturday the 17th (moved there from Friday the 16th), dragged to Sunday the 18th: all one day later.
        await planner.UpdateAsync(weekly.Id, new UpdatePlannerItemRequest(Date: Oct18), new PlannerTarget(PlannerEditScope.All, Oct16));

        var first = (await RangeAsync(planner, Oct9, Oct11)).Single();
        Assert.Equal(Oct10, first.Date);
    }

    [Fact]
    public async Task The_heads_up_says_how_long_it_lasts_and_goes_in_the_planner_channel()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        (await config.GetOrCreateAsync()).TimeZone = "UTC";
        db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = DevicePlatforms.Android });
        await db.SaveChangesAsync();
        var push = new FakePushSender();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero));
        var dispatcher = new PlannerAlertDispatcher(db, push, config, clock, NullLogger<PlannerAlertDispatcher>.Instance);
        var planner = new PlannerService(db);
        await planner.CreateAsync(new CreatePlannerItemRequest("Goa trip", Oct9, EndDate: Oct11, NotifyBeforeMinutes: 30));
        await planner.CreateAsync(new CreatePlannerItemRequest(
            "Conference", Oct9, ScheduledTime: new TimeOnly(9, 0), EndTime: new TimeOnly(13, 0), EndDate: Oct10, NotifyBeforeMinutes: 30));

        Assert.Equal(2, await dispatcher.DispatchDueAsync());

        Assert.Equal(["From today to Sun 11 Oct", "Starts in 30 minutes · 9:00 am · until Sat 10 Oct"], push.SentBodies.Order());
        Assert.All(push.SentChannels, c => Assert.Equal(PushChannels.PlannerEvents, c));
    }

    [Fact]
    public async Task An_old_occurrence_moved_and_made_longer_into_the_range_still_shows()
    {
        var (_, planner) = Setup();
        var trip = await TripAsync(planner, "FREQ=WEEKLY");

        // The first weekend made to last until Monday the 12th reaches into a range that starts then.
        await planner.UpdateAsync(trip.Id, new UpdatePlannerItemRequest(EndDate: Oct12), new PlannerTarget(PlannerEditScope.This, Oct9));

        var monday = Assert.Single(await RangeAsync(planner, Oct12, Oct12));
        Assert.Equal(Oct9, monday.OccurrenceDate);
    }
}
