using PersonaOS.Application.Ai;
using PersonaOS.Application.Planner;

namespace PersonaOS.Tests.Planner;

/// <summary>
/// A routine pasted as a table becomes calendar items in code (production chat, 2026-11-03:
/// qwen2.5:3b made board tasks, a goal, at most 16 items, no repeat, and "1:10" after lunch at 01:10).
/// </summary>
public class ScheduleRequestTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    // The routine from the production chat, rows as pasted (tab between time and activity).
    private const string Routine = """
        Block my calendar for daily repeatable tasks till DEC 31, 2026 with the following schedules

        Time	Activity
        6:00–6:05	Wake up, make bed
        6:05–6:10	Prayer (3 min) + journaling (2 min)
        8:45–9:15	Commute
        9:15–12:45	Focused work
        12:45–1:10	Lunch
        1:10–1:15	Transition
        1:45–6:15	Work
        6:15–6:45	Commute to gym
        9:30–9:40	Plan tomorrow
        10:00–10:05	Meditation, lights out
        """;

    [Fact]
    public void Every_row_is_read_with_times_that_run_forward_through_the_day()
    {
        var schedule = ScheduleRequest.Read(Routine, Today)!;

        Assert.Equal(10, schedule.Rows.Count);
        Assert.Equal(new ScheduleRequest.Row("Wake up, make bed", new(6, 0), new(6, 5)), schedule.Rows[0]);
        Assert.Equal(new ScheduleRequest.Row("Focused work", new(9, 15), new(12, 45)), schedule.Rows[3]);
        Assert.Equal(new ScheduleRequest.Row("Lunch", new(12, 45), new(13, 10)), schedule.Rows[4]);
        Assert.Equal(new ScheduleRequest.Row("Transition", new(13, 10), new(13, 15)), schedule.Rows[5]);
        Assert.Equal(new ScheduleRequest.Row("Commute to gym", new(18, 15), new(18, 45)), schedule.Rows[7]);
        Assert.Equal(new ScheduleRequest.Row("Meditation, lights out", new(22, 0), new(22, 5)), schedule.Rows[9]);
    }

    [Fact]
    public void The_repeat_and_its_last_day_come_from_the_users_words()
    {
        var schedule = ScheduleRequest.Read(Routine, Today)!;

        Assert.Equal("daily", schedule.Repeat);
        Assert.Equal(new DateOnly(2026, 12, 31), schedule.Until);
        Assert.False(schedule.NoNotification);
    }

    [Fact]
    public void An_am_or_pm_the_user_wrote_wins()
    {
        var schedule = ScheduleRequest.Read("""
            6:00–6:05 AM	Wake up
            9:15 AM–12:45 PM	Focused work
            12:45–1:10 PM	Lunch
            11:30–12:30 PM	Call
            """, Today)!;

        Assert.Equal(new TimeOnly(6, 0), schedule.Rows[0].Start);
        Assert.Equal(new TimeOnly(12, 45), schedule.Rows[1].End);
        Assert.Equal(new TimeOnly(12, 45), schedule.Rows[2].Start);
        Assert.Equal(new TimeOnly(13, 10), schedule.Rows[2].End);
        Assert.Equal(new TimeOnly(23, 30), schedule.Rows[3].Start);
    }

    [Fact]
    public void A_list_of_start_times_runs_each_row_until_the_next()
    {
        // As the owner sent it, 2026-11-03.
        var schedule = ScheduleRequest.Read("""
            use this schedule to add recurring calendar event till Dec31, 2026
            * Wake up, make bed at 06:00 AM
            * Prayer (3 min) + journaling (2 min) at 06:05 AM
            * Lunch at 12:45 PM
            * Transition at 1:10 PM
            * Meditation, lights out at 10:00 PM
            """, Today)!;

        Assert.Equal(5, schedule.Rows.Count);
        Assert.Equal(new ScheduleRequest.Row("Prayer (3 min) + journaling (2 min)", new(6, 5), new(12, 45)), schedule.Rows[1]);
        Assert.Equal(new ScheduleRequest.Row("Transition", new(13, 10), new(22, 0)), schedule.Rows[3]);
        Assert.Null(schedule.Rows[4].End);
        Assert.Equal("daily", schedule.Repeat);
        Assert.Equal(new DateOnly(2026, 12, 31), schedule.Until);
    }

    [Fact]
    public void No_notification_said_in_another_message_is_kept()
    {
        var schedule = ScheduleRequest.Read(Routine, Today, ["no notification required. but add everything in the list now"])!;

        Assert.True(schedule.NoNotification);
    }

    [Theory]
    [InlineData("Lunch with Marco at 1pm tomorrow")]
    [InlineData("Meeting 9:00–10:00 then gym 18:00–19:00")]
    public void A_message_with_fewer_than_three_rows_is_not_a_routine(string message) =>
        Assert.Null(ScheduleRequest.Read(message, Today));

    [Fact]
    public void A_later_message_that_points_back_uses_the_routine_sent_before()
    {
        var schedule = ChatService.ScheduleFor("go ahead create items to the calendar", [Routine], Today);

        Assert.NotNull(schedule);
        Assert.Equal(10, schedule.Rows.Count);
    }

    [Theory]
    [InlineData("Put these on my board as tasks")]
    [InlineData("thanks, that's all for now")]
    public void A_message_that_does_not_ask_for_the_calendar_leaves_it_to_the_model(string message) =>
        Assert.Null(ChatService.ScheduleFor(message, [Routine], Today));
}
