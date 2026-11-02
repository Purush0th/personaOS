using System.Text.Json;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Reminders;
using PersonaOS.Application.Reminders.Tools;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>
/// qwen2.5:3b put "in 2 hours" at the wrong time, "Thursday" on the Friday, and proposed reminders
/// with times nobody gave (accuracy suite, 2026-11-02). Times are now read in code.
/// </summary>
public class UserTimeGuardTests
{
    // Monday 2 November 2026, 09:18.
    private static readonly DateTime Now = new(2026, 11, 2, 9, 18, 40);

    private static ChatTurnState Turn(string message, params string[] earlier) =>
        new("test-model", new HashSet<string> { "add_planner_item", "create_reminder", "create_task" },
            ["add_planner_item", "create_reminder", "create_task"],
            userMessage: message, userToday: DateOnly.FromDateTime(Now), userNow: Now, earlierUserMessages: earlier);

    private static Task<ToolCallVerdict?> Check(ChatTurnState turn, string tool, string input) =>
        new UserTimeGuard().CheckAsync(new AiToolCall("1", tool, input), turn, default);

    private static string Field(ToolCallVerdict? verdict, string name) =>
        JsonDocument.Parse(Assert.IsType<ToolCallVerdict.Rewrite>(verdict).Call.InputJson).RootElement.GetProperty(name).GetString()!;

    [Theory]
    [InlineData("Remind me in 2 hours to stretch", "2026-11-02T11:18:00")]
    [InlineData("remind me in 20 minutes to check the oven", "2026-11-02T09:38:00")]
    [InlineData("Remind me in half an hour", "2026-11-02T09:48:00")]
    [InlineData("Remind me on Thursday at 10am to submit the report", "2026-11-05T10:00:00")]
    [InlineData("Remind me to call mum at 7pm tomorrow", "2026-11-03T19:00:00")]
    [InlineData("ping me the day after tomorrow at 8:30 am", "2026-11-04T08:30:00")]
    [InlineData("Remind me at noon on Saturday", "2026-11-07T12:00:00")]
    public async Task A_reminder_is_set_to_the_time_the_user_said(string message, string expected)
    {
        var verdict = await Check(Turn(message), "create_reminder", """{"message":"x","dueAtLocal":"2026-11-06T11:00:00"}""");

        Assert.Equal(expected, Field(verdict, "dueAtLocal"));
    }

    [Fact]
    public async Task A_reminder_already_at_the_right_time_is_let_through()
    {
        Assert.Null(await Check(Turn("Remind me to call mum at 7pm tomorrow"), "create_reminder", """{"message":"Call mum","dueAtLocal":"2026-11-03T19:00:00"}"""));
    }

    [Theory]
    [InlineData("Remind me to pay the rent")]
    [InlineData("Remind me tomorrow to pay the rent")]
    public async Task A_reminder_with_no_time_given_is_refused_with_ask_when(string message)
    {
        var turn = Turn(message);
        var verdict = await Check(turn, "create_reminder", """{"message":"Pay the rent","dueAtLocal":"2026-11-03T18:00:00"}""");

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
        Assert.Contains("ask them when", answer.Content);
        Assert.Single(turn.Refusals);
    }

    [Fact]
    public async Task A_time_given_in_answer_to_the_question_counts()
    {
        var verdict = await Check(Turn("Tomorrow at 9am", "Remind me to pay the rent"), "create_reminder", """{"message":"Pay the rent","dueAtLocal":"2026-11-03T09:00:00"}""");

        Assert.Null(verdict);
    }

    [Theory]
    [InlineData("Remind me this evening to water the plants")]
    [InlineData("Remind me tonight to lock the door")]
    public async Task A_part_of_the_day_says_when(string message)
    {
        Assert.IsNotType<ToolCallVerdict.Answer>(await Check(Turn(message), "create_reminder", """{"message":"x","dueAtLocal":"2026-11-02T20:00:00"}"""));
    }

    [Theory]
    [InlineData("Remind me next Friday at 10am")]
    [InlineData("Remind me on Monday at 10am")] // Monday is today: this week's or the next?
    [InlineData("Remind me at 9 to call")]
    [InlineData("Remind me on Tuesday or Wednesday at 5pm")]
    public async Task What_is_not_certain_is_left_to_the_model(string message)
    {
        var verdict = await Check(Turn(message), "create_reminder", """{"message":"x","dueAtLocal":"2026-11-10T10:00:00"}""");

        Assert.True(verdict is null || Field(verdict, "dueAtLocal") == "2026-11-10T10:00:00");
    }

    [Fact]
    public async Task An_event_moves_to_the_weekday_and_time_and_keeps_its_length()
    {
        var verdict = await Check(Turn("Add a meeting with Priya on Friday at 3pm"), "add_planner_item",
            """{"title":"Meeting with Priya","date":"2026-11-07","scheduledTime":"14:00","endTime":"15:00"}""");

        Assert.Equal("2026-11-06", Field(verdict, "date"));
        Assert.Equal("15:00", Field(verdict, "scheduledTime"));
        Assert.Equal("16:00", Field(verdict, "endTime"));
    }

    [Fact]
    public async Task An_all_day_event_gets_the_time_the_user_gave()
    {
        var verdict = await Check(Turn("I have a dentist appointment tomorrow at 4pm, put it on my calendar"), "add_planner_item",
            """{"title":"Dentist","date":"2026-11-03"}""");

        Assert.Equal("16:00", Field(verdict, "scheduledTime"));
    }

    [Fact]
    public async Task Other_tools_are_left_alone()
    {
        Assert.Null(await Check(Turn("Add a task to call Sam at 3pm"), "create_task", """{"title":"Call Sam"}"""));
    }
}

public class TaskTitleGuardTests
{
    private static ChatTurnState Turn(string message, params string[] earlier) =>
        new("test-model", new HashSet<string> { "create_task" }, ["create_task"], userMessage: message, earlierUserMessages: earlier);

    private static Task<ToolCallVerdict?> Check(ChatTurnState turn, string title) =>
        new TaskTitleGuard().CheckAsync(new AiToolCall("1", "create_task", JsonSerializer.Serialize(new { title })), turn, default);

    [Theory]
    [InlineData("Add a task to my backlog", "Task for later")]
    [InlineData("Create a new task", "New task")]
    [InlineData("Add a task to my backlog", "Book the car service")]
    public async Task A_title_the_user_never_said_is_refused(string message, string title)
    {
        var turn = Turn(message);

        Assert.True(Assert.IsType<ToolCallVerdict.Answer>(await Check(turn, title)).IsError);
        Assert.Single(turn.Refusals);
    }

    [Theory]
    [InlineData("Add a task to follow up with Ravi tomorrow.", "Follow up with Ravi")]
    [InlineData("Put 'renew the passport' on my board.", "Renew passport")]
    [InlineData("Book the car service", "Book car service")]
    public async Task A_title_in_the_users_words_passes(string message, string title)
    {
        Assert.Null(await Check(Turn(message), title));
    }

    [Fact]
    public async Task The_title_may_come_from_the_message_before()
    {
        Assert.Null(await Check(Turn("yes please", "Add a task to renew my passport"), "Renew passport"));
    }
}

public class FocusQuestionTests
{
    [Theory]
    [InlineData("I will create a quarterly goal for reading 12 books. Would you like to proceed?", false)]
    [InlineData("Shall I add it to your backlog?", false)]
    [InlineData("What time should I remind you?", true)]
    [InlineData("Would you like it at 9am or 10am?", true)]
    [InlineData("Done.", false)]
    public void Asking_leave_is_not_a_question_for_the_user(string reply, bool asks)
    {
        Assert.Equal(asks, ChatService.AsksTheUser(reply));
    }

    [Theory]
    [InlineData("What is on my calendar tomorrow?", "get_planner")]
    [InlineData("When is my haircut appointment?", "get_planner")]
    [InlineData("What did I note about the electrician?", "get_notes")]
    [InlineData("Which reminders do I have?", "get_reminders")]
    [InlineData("What are my goals?", null)]
    public void A_question_reads_the_data_the_prompt_does_not_hold(string message, string? tool)
    {
        Assert.Equal(tool, ChatService.QuestionReadTool(message, ["get_planner", "get_notes", "get_reminders", "get_goals"]));
    }
}

public class ReminderMatchTests
{
    private static ReminderDto Reminder(int id, string message) =>
        new(id, message, DateTime.UtcNow, DateTime.Now, "pending", null, null, null, null, null, DateTime.UtcNow);

    [Fact]
    public void Words_find_the_one_reminder_they_name()
    {
        var list = new[] { Reminder(4, "Call the bank about the card"), Reminder(7, "Water the plants") };

        Assert.Equal(4, Assert.Single(ReminderMatch.Best(list, "call the bank")).Id);
    }

    [Fact]
    public void Words_that_match_nothing_find_nothing()
    {
        Assert.Empty(ReminderMatch.Best([Reminder(4, "Call the bank")], "dentist"));
    }
}

public class GroundingTests
{
    [Fact]
    public void An_appointment_nothing_read_mentions_is_not_found()
    {
        var results = new[] { """{"items":[{"title":"Read a book today","status":"planned"}]}""" };

        Assert.Equal("haircut", ChatService.NotFound("When is my haircut appointment?", results, "Your haircut appointment is planned for today."));
    }

    [Fact]
    public void A_reply_that_says_it_found_nothing_stands()
    {
        Assert.Null(ChatService.NotFound("When is my haircut appointment?", ["{}"], "You have no haircut appointment in your planner."));
    }

    [Fact]
    public void Something_the_data_mentions_is_found()
    {
        Assert.Null(ChatService.NotFound("When is my meeting with Priya?", ["""{"title":"Meeting with Priya"}"""], "Friday at 3pm."));
    }

    [Fact]
    public void A_question_with_nothing_to_look_for_is_left_alone()
    {
        Assert.Null(ChatService.NotFound("What is on my calendar tomorrow?", ["{}"], "Nothing planned."));
    }
}

public class PlannerReadDayTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 18, 0);

    private static Task<ToolCallVerdict?> Check(string message, string input) =>
        new UserTimeGuard().CheckAsync(new AiToolCall("1", "get_planner", input),
            new ChatTurnState("m", new HashSet<string>(), ["get_planner"], userMessage: message, userToday: DateOnly.FromDateTime(Now), userNow: Now), default);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"date":"2026-11-02"}""")]
    public async Task A_read_for_tomorrow_reads_tomorrow(string input)
    {
        var verdict = await Check("What is on my calendar tomorrow?", input);

        Assert.Contains("2026-11-03", Assert.IsType<ToolCallVerdict.Rewrite>(verdict).Call.InputJson);
    }

    [Fact]
    public async Task Today_with_no_date_is_right()
    {
        Assert.Null(await Check("What is on my calendar today?", "{}"));
    }
}

public class TaskRequestTests
{
    [Theory]
    [InlineData("Create a to-do for following up with Ravi.", "Following up with Ravi")]
    [InlineData("Add a task to call the bank", "Call the bank")]
    [InlineData("Put 'renew the passport' on my board.", "Renew the passport")]
    [InlineData("Add a task to my backlog to book the car service", "Book the car service")]
    [InlineData("Add a task to my backlog", null)]
    [InlineData("What tasks do I have?", null)]
    public void The_title_is_read_from_the_users_words(string message, string? title)
    {
        Assert.Equal(title, PersonaOS.Application.Board.TaskRequest.TitleOf(message));
    }

    [Fact]
    public async Task A_missing_title_is_filled_from_the_message()
    {
        var turn = new ChatTurnState("m", new HashSet<string> { "create_task" }, ["create_task"], userMessage: "Create a to-do for following up with Ravi.");

        var verdict = await new TaskTitleGuard().CheckAsync(new AiToolCall("1", "create_task", """{"title":"","priority":"high"}"""), turn, default);

        var input = JsonDocument.Parse(Assert.IsType<ToolCallVerdict.Rewrite>(verdict).Call.InputJson).RootElement;
        Assert.Equal("Following up with Ravi", input.GetProperty("title").GetString());
        Assert.Equal("high", input.GetProperty("priority").GetString());
    }
}

public class TaskCommandTests
{
    [Theory]
    [InlineData("Mark TASK-11 as done", "TASK-11", "done")]
    [InlineData("move task-4 into in progress", "TASK-4", "in_progress")]
    [InlineData("Move TASK-4 to To do", "TASK-4", "todo")]
    [InlineData("Finish TASK-2", "TASK-2", "done")]
    [InlineData("Start working on TASK-9", "TASK-9", "in_progress")]
    [InlineData("I've finished TASK-11", "TASK-11", "done")]
    [InlineData("I'm done with TASK-11.", "TASK-11", "done")]
    [InlineData("TASK-11 is done", "TASK-11", "done")]
    public void A_plain_move_is_read(string message, string key, string column)
    {
        Assert.Equal((key, column), PersonaOS.Application.Board.TaskCommand.Parse(message));
    }

    [Theory]
    [InlineData("Move TASK-4 to done and TASK-5 to backlog")]
    [InlineData("What is TASK-4 about?")]
    [InlineData("Is TASK-4 done?")]
    public void Anything_else_is_left_to_the_model(string message)
    {
        Assert.Null(PersonaOS.Application.Board.TaskCommand.Parse(message));
    }

    [Theory]
    [InlineData("Where can I download PersonaOS?", true)]
    [InlineData("How do I install the app on my phone?", true)]
    [InlineData("Download the report", false)]
    public void Asking_where_to_get_PersonaOS_is_recognised(string message, bool asks)
    {
        Assert.Equal(asks, ChatService.AsksWhereToGetPersonaOs(message));
    }
}

public sealed class ReminderCommandTests
{
    [Theory]
    [InlineData("Cancel my reminder to call the bank", "call the bank")]
    [InlineData("Forget the reminder about the dentist.", "the dentist")]
    [InlineData("please delete my laundry reminder", "laundry")]
    [InlineData("Cancel all reminders", null)]
    [InlineData("Remind me to call the bank", null)]
    [InlineData("Why did my reminder not ring?", null)]
    public void Reads_the_reminder_a_message_cancels(string message, string? words)
    {
        Assert.Equal(words, ReminderCommand.CancelWords(message));
    }
}

public sealed class MemoryWordsGuardTests
{
    private static Task<ToolCallVerdict?> Check(string message, string content, IReadOnlyList<string>? earlier = null) =>
        new MemoryWordsGuard().CheckAsync(
            new AiToolCall("1", "create_memory", JsonSerializer.Serialize(new { content })),
            new ChatTurnState("m", new HashSet<string>(), ["create_memory"], userMessage: message, earlierUserMessages: earlier),
            default);

    [Fact]
    public async Task A_memory_made_up_from_thanks_is_refused()
    {
        var verdict = await Check("Thanks, that's all for now", "The user appreciated the update");
        Assert.IsType<ToolCallVerdict.Answer>(verdict);
    }

    [Theory]
    [InlineData("The user thanked me and said that's all for now")]
    [InlineData("User is grateful for the help, all good for now")]
    public async Task A_memory_made_of_the_courtesy_itself_is_refused(string content)
    {
        Assert.IsType<ToolCallVerdict.Answer>(await Check("Thanks, that's all for now", content));
    }

    [Fact]
    public async Task A_memory_from_the_users_words_passes()
    {
        Assert.Null(await Check("Remember that my sister's name is Priya", "User's sister is called Priya"));
    }

    [Fact]
    public async Task A_memory_from_an_earlier_message_passes()
    {
        Assert.Null(await Check("Yes, keep that", "User is vegetarian", ["I'm vegetarian, by the way"]));
    }
}

public sealed class CalendarRequestGuardTests
{
    private static Task<ToolCallVerdict?> Check(string message) =>
        new CalendarRequestGuard().CheckAsync(
            new AiToolCall("1", "create_reminder", """{"message":"Call with Anna","dueAtLocal":"2026-11-07T11:00"}"""),
            new ChatTurnState("m", new HashSet<string> { "create_reminder", "add_planner_item" }, ["create_reminder", "add_planner_item"], userMessage: message),
            default);

    [Fact]
    public async Task A_call_to_schedule_becomes_a_planner_item_at_the_same_time()
    {
        var verdict = Assert.IsType<ToolCallVerdict.Rewrite>(await Check("Schedule a call with Anna on Saturday at 11am"));
        Assert.Equal("add_planner_item", verdict.Call.Name);
        using var doc = JsonDocument.Parse(verdict.Call.InputJson);
        Assert.Equal("Call with Anna", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("2026-11-07", doc.RootElement.GetProperty("date").GetString());
        Assert.Equal("11:00", doc.RootElement.GetProperty("scheduledTime").GetString());
    }

    [Theory]
    [InlineData("Remind me to schedule a call with Anna at 11am")]
    [InlineData("Ping me at 11 about Anna")]
    public async Task A_reminder_asked_for_stays_a_reminder(string message)
    {
        Assert.Null(await Check(message));
    }
}

public sealed class PlannerRangeTests
{
    [Fact]
    public async Task A_range_is_narrowed_to_the_one_day_named()
    {
        var now = new DateTime(2026, 11, 2, 10, 0, 0);
        var verdict = await new UserTimeGuard().CheckAsync(
            new AiToolCall("1", "get_planner", """{"from":"2026-11-02","to":"2026-11-03"}"""),
            new ChatTurnState("m", new HashSet<string>(), ["get_planner"], userMessage: "What is on my calendar tomorrow?", userToday: DateOnly.FromDateTime(now), userNow: now),
            default);

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        Assert.Equal("""{"date":"2026-11-03"}""", rewrite.Call.InputJson);
    }
}

public sealed class PlannerCommandTests
{
    // A Monday morning.
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0);

    [Theory]
    [InlineData("Book lunch with Marco tomorrow at 1pm", "Lunch with Marco", "2026-11-03", "13:00")]
    [InlineData("Schedule a call with Anna on Saturday at 11am", "Call with Anna", "2026-11-07", "11:00")]
    [InlineData("Please book a haircut on Friday", "Haircut", "2026-11-06", null)]
    public void A_plain_booking_is_read(string message, string title, string date, string? time)
    {
        var booking = PersonaOS.Application.Planner.PlannerCommand.Parse(message, Now);

        Assert.NotNull(booking);
        Assert.Equal(title, booking.Title);
        Assert.Equal(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), booking.Date);
        Assert.Equal(time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), booking.Time);
    }

    [Theory]
    [InlineData("Book lunch with Marco")]
    [InlineData("Add a task to book the car service tomorrow")]
    [InlineData("Schedule a reminder for tomorrow at 9")]
    [InlineData("Did I book lunch tomorrow?")]
    public void Anything_else_is_left_to_the_model(string message)
    {
        Assert.Null(PersonaOS.Application.Planner.PlannerCommand.Parse(message, Now));
    }
}

public sealed class WhenPhraseMonthTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0);

    [Theory]
    [InlineData("Lunch with Marco tomorrow at 1pm")]
    [InlineData("Remind Mark tomorrow at 1pm")]
    [InlineData("Decide on the junk tomorrow at 1pm")]
    public void A_name_that_starts_like_a_month_is_not_a_date(string message)
    {
        Assert.Equal(new DateOnly(2026, 11, 3), WhenPhrase.Read(message, Now)?.Day);
    }

    [Theory]
    [InlineData("Dinner on 3 March tomorrow at 7pm")]
    [InlineData("Dinner on the 3rd at 7pm")]
    public void A_real_date_leaves_the_day_to_the_model(string message)
    {
        Assert.Null(WhenPhrase.Read(message, Now)?.Day);
    }
}
