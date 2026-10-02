using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>
/// Asked for "today" or "tomorrow", qwen2.5:3b wrote the day after in about one try in five
/// (accuracy suite, 2026-10-01). The guard sends such a call back with the right date.
/// </summary>
public class RelativeDateGuardTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static ChatTurnState Turn(string message) =>
        new("test-model", new HashSet<string> { "add_planner_item", "create_reminder" }, ["add_planner_item", "create_reminder"],
            userMessage: message, userToday: Today);

    private static Task<ToolCallVerdict?> Check(string message, string tool, string input) =>
        new RelativeDateGuard().CheckAsync(new AiToolCall("1", tool, input), Turn(message), default);

    [Fact]
    public async Task A_planner_item_for_tomorrow_when_the_user_said_today_is_sent_back_with_today()
    {
        var verdict = await Check("Add 'Buy milk' to my planner for today", "add_planner_item", """{"title":"Buy milk","date":"2026-10-02"}""");

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
        Assert.Contains("2026-10-01", answer.Content);
        Assert.Contains("Thursday", answer.Content);
    }

    [Fact]
    public async Task A_reminder_two_days_out_when_the_user_said_tomorrow_is_sent_back()
    {
        var verdict = await Check("Remind me to call mum at 7pm tomorrow", "create_reminder", """{"message":"Call mum","dueAtLocal":"2026-10-03T19:00:00"}""");

        Assert.Contains("2026-10-02", Assert.IsType<ToolCallVerdict.Answer>(verdict).Content);
    }

    [Theory]
    [InlineData("Add 'Buy milk' to my planner for today", """{"title":"Buy milk","date":"2026-10-01"}""")]
    [InlineData("Add 'Buy milk' to my planner for today", """{"title":"Buy milk"}""")]
    [InlineData("Move it from today to tomorrow", """{"title":"Buy milk","date":"2026-10-02"}""")]
    [InlineData("Plan 'Buy milk' for Friday", """{"title":"Buy milk","date":"2026-10-02"}""")]
    [InlineData("Plan 'Buy milk' for 2026-10-05, not today", """{"title":"Buy milk","date":"2026-10-05"}""")]
    [InlineData("Add 'Buy milk' to my planner", """{"title":"Buy milk","date":"2026-10-04"}""")]
    public async Task Right_days_and_uncertain_messages_pass(string message, string input)
    {
        Assert.Null(await Check(message, "add_planner_item", input));
    }

    [Fact]
    public async Task A_planner_item_with_no_day_when_the_user_said_tomorrow_is_sent_back()
    {
        // No day means today, which is not what was asked for.
        var verdict = await Check("Plan 'Buy milk' for tomorrow", "add_planner_item", """{"title":"Buy milk"}""");

        Assert.Contains("2026-10-02", Assert.IsType<ToolCallVerdict.Answer>(verdict).Content);
    }

    [Fact]
    public async Task Yesterday_is_held_to_yesterday_so_the_past_is_refused_as_such()
    {
        var verdict = await Check("Remind me yesterday at 9am to call the bank", "create_reminder", """{"message":"Call the bank","dueAtLocal":"2026-10-02T09:00:00"}""");

        Assert.Contains("2026-09-30", Assert.IsType<ToolCallVerdict.Answer>(verdict).Content);
    }

    [Fact]
    public async Task A_read_is_never_checked()
    {
        var turn = new ChatTurnState("m", new HashSet<string>(), ["get_planner"], userMessage: "what's on today?", userToday: Today);

        Assert.Null(await new RelativeDateGuard().CheckAsync(new AiToolCall("1", "get_planner", """{"date":"2026-10-02"}"""), turn, default));
    }
}
