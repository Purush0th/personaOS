using System.Text.Json;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>What holdout3 (2026-11-02) found: each class is one failure, fixed in code.</summary>
public sealed class DayAfterTomorrowTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Fact]
    public void The_day_after_tomorrow_is_not_read_as_tomorrow()
    {
        Assert.Equal(("the day after tomorrow", Today.AddDays(2)), RelativeDateGuard.IntendedDay("Schedule a call with the plumber the day after tomorrow at 9am", Today));
    }

    [Fact]
    public async Task A_call_for_the_day_after_tomorrow_passes()
    {
        var verdict = await new RelativeDateGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"title":"Call with the plumber","date":"2026-11-04","scheduledTime":"09:00"}"""),
            new ChatTurnState("m", new HashSet<string> { "add_planner_item" }, ["add_planner_item"],
                userMessage: "Schedule a call with the plumber the day after tomorrow at 9am", userToday: Today),
            default);

        Assert.Null(verdict);
    }

    [Fact]
    public void The_booking_title_drops_the_words_around_the_day()
    {
        var booking = PlannerCommand.Parse("Schedule a call with the plumber the day after tomorrow at 9am", new DateTime(2026, 11, 2, 9, 0, 0));

        Assert.NotNull(booking);
        Assert.Equal("Call with the plumber", booking.Title);
        Assert.Equal(Today.AddDays(2), booking.Date);
        Assert.Equal(new TimeOnly(9, 0), booking.Time);
    }
}

public sealed class PlannerCallFixTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0);
    private const string Message = "Haircut on Thursday at 11am - add it to my calendar";

    private static ChatTurnState Turn(string message = Message) =>
        new("m", new HashSet<string> { "add_planner_item" }, ["add_planner_item"], userMessage: message, userToday: DateOnly.FromDateTime(Now), userNow: Now);

    [Fact]
    public async Task A_filler_end_day_does_not_keep_the_wrong_day()
    {
        var verdict = await new UserTimeGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"title":"Haircut","date":"2026-11-06","endDate":"2026-11-06","scheduledTime":"11:00"}"""),
            Turn(), default);

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        using var doc = JsonDocument.Parse(rewrite.Call.InputJson);
        Assert.Equal("2026-11-05", doc.RootElement.GetProperty("date").GetString());
        Assert.False(doc.RootElement.TryGetProperty("endDate", out _));
    }

    [Fact]
    public async Task Links_nobody_asked_for_are_dropped()
    {
        var verdict = await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"title":"Haircut","date":"2026-11-05","taskKey":"TASK-1","goalKey":"GOAL-2"}"""),
            Turn(), default);

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        Assert.Equal("""{"title":"Haircut","date":"2026-11-05"}""", rewrite.Call.InputJson);
    }

    [Theory]
    [InlineData("Plan TASK-7 for tomorrow morning")]
    [InlineData("Block an hour tomorrow for my Rust goal")]
    public async Task A_link_the_user_spoke_of_stays(string message)
    {
        Assert.Null(await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"title":"Work","date":"2026-11-03","taskKey":"TASK-7"}"""),
            Turn(message), default));
    }

    [Fact]
    public async Task A_task_named_by_the_user_may_lend_its_title()
    {
        Assert.Null(await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"date":"2026-11-03","taskKey":"TASK-7"}"""),
            Turn("Plan TASK-7 for tomorrow morning"), default));
    }

    [Theory]
    [InlineData(Message, "Haircut")]
    [InlineData("Add 'Buy milk' to my planner for today", "Buy milk")]
    public async Task An_unasked_link_with_no_title_takes_the_title_from_the_message(string message, string title)
    {
        // The fine-tuned qwen3:4b proposed {"taskKey":"TASK-7","goalKey":"GOAL-1"} with no title, and the
        // card showed the task instead of the event (2026-11-07).
        var verdict = await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"date":"2026-11-05","scheduledTime":"11:00","taskKey":"TASK-7","goalKey":"GOAL-1"}"""),
            Turn(message), default);

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        using var doc = JsonDocument.Parse(rewrite.Call.InputJson);
        Assert.Equal(title, doc.RootElement.GetProperty("title").GetString());
        Assert.False(doc.RootElement.TryGetProperty("taskKey", out _));
        Assert.False(doc.RootElement.TryGetProperty("goalKey", out _));
    }

    [Fact]
    public async Task An_unasked_link_with_no_title_to_be_found_asks_the_model_for_one()
    {
        var verdict = await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"date":"2026-11-02","taskKey":"TASK-7"}"""),
            Turn("Yes, put that on my calendar"), default);

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
        Assert.Contains("title", answer.Content, StringComparison.Ordinal);
    }

    private static ChatTurnState ReminderTurn(string message) =>
        new("m", new HashSet<string> { "add_planner_item", "create_reminder" }, ["add_planner_item", "create_reminder"],
            userMessage: message, userToday: DateOnly.FromDateTime(Now), userNow: Now);

    [Theory]
    [InlineData("""{"title":"Move laundry","date":"2026-11-02","scheduledTime":"09:30"}""", "Move laundry")]
    [InlineData("""{"date":"2026-11-02","scheduledTime":"09:30"}""", "Move the laundry")]
    public async Task A_planner_item_for_a_nudge_becomes_a_reminder_at_the_time_said(string input, string text)
    {
        // The fine-tuned qwen3:4b proposed a planner item for this in every run (2026-11-07).
        var verdict = await new ReminderNotEventGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", input), ReminderTurn("In 30 minutes, nudge me to move the laundry"), default);

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        Assert.Equal("create_reminder", rewrite.Call.Name);
        using var doc = JsonDocument.Parse(rewrite.Call.InputJson);
        Assert.Equal(text, doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("2026-11-02T09:30:00", doc.RootElement.GetProperty("dueAtLocal").GetString());
    }

    [Theory]
    [InlineData("Put the dentist on my calendar tomorrow at 4pm and remind me")]
    [InlineData("Schedule a call with Anna on Saturday at 11am")]
    [InlineData("Remind me to move the laundry")]
    public async Task A_planner_item_the_user_asked_for_stays(string message)
    {
        Assert.Null(await new ReminderNotEventGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"title":"Dentist","date":"2026-11-03","scheduledTime":"16:00"}"""),
            ReminderTurn(message), default));
    }

    [Fact]
    public async Task An_untitled_planner_call_for_a_nudge_is_pointed_at_a_reminder()
    {
        // The fine-tuned qwen3:4b answered "In 30 minutes, nudge me to move the laundry" with untitled
        // planner items until it gave up (2026-11-07).
        var turn = new ChatTurnState("m", new HashSet<string> { "add_planner_item", "create_reminder" }, ["add_planner_item", "create_reminder"],
            userMessage: "In 30 minutes, nudge me to move the laundry", userToday: DateOnly.FromDateTime(Now), userNow: Now);

        var verdict = await new UnaskedLinkGuard().CheckAsync(
            new AiToolCall("1", "add_planner_item", """{"date":"2026-11-02","scheduledTime":"09:30","taskKey":"TASK-7","goalKey":"GOAL-1"}"""),
            turn, default);

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.Contains("create_reminder", answer.Content, StringComparison.Ordinal);
    }
}

public sealed class StatedEventTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0);

    [Theory]
    [InlineData("I have a dentist appointment tomorrow at 4pm, put it on my calendar", "Dentist appointment", "2026-11-03", "16:00")]
    [InlineData("Haircut on Thursday at 11am - add it to my calendar", "Haircut", "2026-11-05", "11:00")]
    [InlineData("I've got the school play on Friday. Can you add that to my planner?", "School play", "2026-11-06", null)]
    public void An_event_said_then_added_is_read(string message, string title, string date, string? time)
    {
        var booking = PlannerCommand.Parse(message, Now);

        Assert.NotNull(booking);
        Assert.Equal(title, booking.Title);
        Assert.Equal(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), booking.Date);
        Assert.Equal(time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), booking.Time);
    }

    [Theory]
    [InlineData("I have a dentist appointment, put it on my calendar")]
    [InlineData("I have a report due tomorrow, put it on my board")]
    [InlineData("Put it on my calendar")]
    public void Without_a_day_or_a_calendar_it_is_left_to_the_model(string message)
    {
        Assert.Null(PlannerCommand.Parse(message, Now));
    }
}

public sealed class OtherDayItemGuardTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0);

    private static async Task<ReplyDraft> ReviewAsync(string message, string reply, Action<PersonaOS.Application.Common.Interfaces.IAppDbContext>? seed = null)
    {
        var db = TestDbContext.Create();
        db.PlannerItems.Add(new PlannerItem { Title = "Read a book today", Date = DateOnly.FromDateTime(Now) });
        seed?.Invoke(db);
        await db.SaveChangesAsync();
        var draft = new ReplyDraft(reply, new ChatTurnState("m", new HashSet<string>(), ["get_planner"], userMessage: message, userToday: DateOnly.FromDateTime(Now), userNow: Now));
        await new OtherDayItemGuard(db).ReviewAsync(draft, CancellationToken.None);
        return draft;
    }

    [Fact]
    public async Task Todays_item_given_as_tomorrows_is_taken_out()
    {
        var draft = await ReviewAsync("Anything planned for tomorrow?", "Today's plan for tomorrow:\n- Read a book today [planned]");

        Assert.Equal("Nothing is planned for Tuesday 3 November.", draft.Text);
    }

    [Fact]
    public async Task Tomorrows_real_items_stay()
    {
        var draft = await ReviewAsync(
            "Anything planned for tomorrow?",
            "Tomorrow you have Dentist at 16:00. You also have Read a book today.",
            db => db.PlannerItems.Add(new PlannerItem { Title = "Dentist", Date = new DateOnly(2026, 11, 3), ScheduledTime = new TimeOnly(16, 0) }));

        Assert.Equal("Tomorrow you have Dentist at 16:00.", draft.Text);
    }

    [Fact]
    public async Task An_answer_about_today_is_left_alone()
    {
        var draft = await ReviewAsync("What is on my calendar today?", "Today: Read a book today.");

        Assert.Equal("Today: Read a book today.", draft.Text);
    }
}

public sealed class TaskEditTests
{
    [Theory]
    [InlineData("Make TASK-11 low priority", "TASK-11", null, null, "low")]
    [InlineData("Set task-3 priority to high", "TASK-3", null, null, "high")]
    [InlineData("Set TASK-11 to 2 points", "TASK-11", null, 2, null)]
    [InlineData("Bump TASK-11 up to 13 points", "TASK-11", null, 13, null)]
    [InlineData("Change TASK-11 to 8 points", "TASK-11", null, 8, null)]
    [InlineData("Rename TASK-11 to Water the herbs", "TASK-11", "Water the herbs", null, null)]
    [InlineData("Change the title of TASK-11 to \"File taxes\".", "TASK-11", "File taxes", null, null)]
    public void A_plain_edit_is_read(string message, string key, string? title, int? points, string? priority)
    {
        Assert.Equal(new TaskCommand.TaskEdit(key, title, points, priority), TaskCommand.Edit(message));
    }

    [Theory]
    [InlineData("Mark TASK-11 as high")]
    [InlineData("What priority is TASK-11?")]
    [InlineData("Set TASK-11 and TASK-12 to 3 points")]
    [InlineData("Mark TASK-11 as done")]
    public void Anything_else_is_left_to_the_model(string message)
    {
        Assert.Null(TaskCommand.Edit(message));
    }

    [Fact]
    public void A_card_that_already_makes_the_change_is_kept()
    {
        var edit = new TaskCommand.TaskEdit("TASK-11", null, null, "low");

        Assert.True(edit.IsMetBy("""{"taskKey":"task-11","priority":"Low"}"""));
        Assert.False(edit.IsMetBy("""{"taskKey":"TASK-11","priority":"high"}"""));
        Assert.False(edit.IsMetBy("""{"taskKey":"TASK-12","priority":"low"}"""));
    }

    [Fact]
    public void The_edit_becomes_update_task_input()
    {
        using var doc = JsonDocument.Parse(new TaskCommand.TaskEdit("TASK-11", null, 5, null).ToJson());
        Assert.Equal("TASK-11", doc.RootElement.GetProperty("taskKey").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("points").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("title", out _));
    }

    [Fact]
    public void A_rename_card_shows_the_new_title()
    {
        var summary = ProposedActionSummary.Describe("update_task", """{"taskKey":"TASK-11","title":"Water the herbs"}""", "TASK-11 “Water the balcony plants”");

        Assert.Contains("title to “Water the herbs”", summary, StringComparison.Ordinal);
    }
}

public sealed class ArithmeticTests
{
    [Theory]
    [InlineData("What is 15% of 80?", "12")]
    [InlineData("How much is 20% of 250?", "50")]
    [InlineData("What's 23 times 4?", "92")]
    [InlineData("what is 120 / 8", "15")]
    [InlineData("Calculate (3 + 4) * 2", "14")]
    [InlineData("What is 10 divided by 4?", "2.5")]
    [InlineData("What's 1,200 minus 350?", "850")]
    public void A_plain_sum_is_worked_out(string message, string answer)
    {
        Assert.Equal(answer, Arithmetic.Parse(message)?.Answer);
    }

    [Theory]
    [InlineData("What is the capital of France?")]
    [InlineData("What is 2026-11-02?")]
    [InlineData("What is on my calendar on 3/11?")]
    [InlineData("What's 5?")]
    [InlineData("What is 10 divided by 0?")]
    [InlineData("Remind me in 20 minutes")]
    public void Anything_else_is_not_a_sum(string message)
    {
        Assert.Null(Arithmetic.Parse(message));
    }

    [Theory]
    [InlineData("15% of 80 is 12.", 12, true)]
    [InlineData("That comes to 1,200.00", 1200, true)]
    [InlineData("15% of 80 is 20.", 12, false)]
    public void The_reply_is_checked_for_the_value(string reply, double value, bool says)
    {
        Assert.Equal(says, Arithmetic.Says(reply, (decimal)value));
    }
}
