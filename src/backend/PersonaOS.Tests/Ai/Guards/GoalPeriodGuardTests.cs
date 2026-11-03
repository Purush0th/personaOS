using System.Text.Json;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>
/// A new goal keeps the period the user named, and with none named the assistant asks (owner,
/// 2026-11-03): qwen2.5:3b otherwise picked a month at random (training capture, 2026-11-02).
/// </summary>
public class GoalPeriodGuardTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static ChatTurnState Turn(string message, params string[] earlier) =>
        new("test-model", new HashSet<string> { "create_goal" }, ["create_goal"],
            userMessage: message, userToday: Today, userNow: Today.ToDateTime(new TimeOnly(9, 0)), earlierUserMessages: earlier);

    private static Task<ToolCallVerdict?> Check(ChatTurnState turn, string input) =>
        new GoalPeriodGuard().CheckAsync(new AiToolCall("1", "create_goal", input), turn, default);

    private static JsonElement Input(ToolCallVerdict? verdict) =>
        JsonDocument.Parse(Assert.IsType<ToolCallVerdict.Rewrite>(verdict).Call.InputJson).RootElement;

    [Fact]
    public async Task A_goal_with_no_period_is_asked_about_not_guessed()
    {
        var turn = Turn("Set a goal: learn basic Spanish");

        var verdict = await Check(turn, """{"title":"Learn basic Spanish","periodType":"month","month":3}""");

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
        Assert.Contains(GoalPeriodGuard.NoPeriodGiven, turn.Refusals);
    }

    [Fact]
    public async Task The_answer_to_the_question_sets_the_period()
    {
        var verdict = await Check(Turn("This quarter", "Set a goal: learn basic Spanish"),
            """{"title":"Learn basic Spanish","periodType":"month","month":3}""");

        var input = Input(verdict);
        Assert.Equal("quarter", input.GetProperty("periodType").GetString());
        Assert.Equal(4, input.GetProperty("quarter").GetInt32());
        Assert.Equal(2026, input.GetProperty("year").GetInt32());
        Assert.False(input.TryGetProperty("month", out _));
    }

    [Theory]
    [InlineData("Set a goal to read 12 books next year", "year", 2027, null, null)]
    [InlineData("I want a goal for this month: walk daily", "month", 2026, null, 11)]
    [InlineData("Goal for next month: no takeaway", "month", 2026, null, 12)]
    [InlineData("A goal for next quarter: ship the beta", "quarter", 2027, 1, null)]
    [InlineData("Set a Q2 goal to save 500", "quarter", 2027, 2, null)]
    [InlineData("Goal: learn to swim by March", "month", 2027, null, 3)]
    [InlineData("Run a marathon in 2028", "year", 2028, null, null)]
    [InlineData("A yearly goal to visit Japan", "year", 2026, null, null)]
    public void A_named_period_is_read(string message, string type, int year, int? quarter, int? month) =>
        Assert.Equal(new GoalPeriodGuard.Period(type, year, quarter, month), GoalPeriodGuard.Read(message, Today));

    [Theory]
    [InlineData("Set a goal: learn basic Spanish")]
    [InlineData("Tell Marco about the goal")]
    [InlineData("I may want to get fit")]
    public void No_period_is_read_where_none_is_named(string message) =>
        Assert.Null(GoalPeriodGuard.Read(message, Today));

    [Fact]
    public async Task A_parent_the_user_did_not_name_is_dropped_with_the_period_they_said()
    {
        var verdict = await Check(Turn("Set a goal to read 12 books next year"),
            """{"title":"Read 12 books","periodType":"month","month":1,"parentKey":"GOAL-2"}""");

        var input = Input(verdict);
        Assert.Equal("year", input.GetProperty("periodType").GetString());
        Assert.False(input.TryGetProperty("parentKey", out _));
    }

    [Fact]
    public async Task A_call_already_on_the_period_said_is_left_alone()
    {
        Assert.Null(await Check(Turn("Set a goal to read 12 books next year"),
            """{"title":"Read 12 books","periodType":"year","year":2027}"""));
    }
}
