using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Board.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Tests.TestSupport;
using static PersonaOS.Tests.TestSupport.TestGoals;

namespace PersonaOS.Tests.Goals;

/// <summary>The assistant's goal tools: what small models send, and cards that could never work.</summary>
public class GoalToolTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (BoardService Board, GoalService Goals) Setup()
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(Today);
        var board = new BoardService(db, new FakeInstanceConfigService(db), new FakePushSender(), clock, NullLogger<BoardService>.Instance);
        return (board, Service(db, clock));
    }

    [Fact]
    public async Task A_task_card_for_a_quarter_goal_is_refused_before_it_is_shown()
    {
        // Seen live on qwen2.5:3b: "add a task under GOAL-2" (a quarter) became a card that
        // would only fail after Confirm.
        var (board, goals) = Setup();
        await goals.CreateAsync(Quarter("Base", 4));

        var ex = await Assert.ThrowsAsync<BoardValidationException>(() =>
            new CreateTaskTool(board, goals).ValidateAsync(Json("""{"title":"Buy shoes","goalKey":"GOAL-1"}""")));

        Assert.Contains("only under monthly goals", ex.Message);
    }

    [Theory]
    [InlineData("\"November\"")]
    [InlineData("\"nov\"")]
    [InlineData("\"11\"")]
    [InlineData("11")]
    public async Task A_month_is_read_however_the_model_writes_it(string month)
    {
        var (_, goals) = Setup();
        await goals.CreateAsync(Quarter("Base", 4));

        var result = await new CreateGoalTool(goals).ExecuteAsync(
            Json($$"""{"title":"Run 10k","periodType":"month","month":{{month}},"parentKey":"GOAL-1"}"""));

        Assert.Contains("Nov 2026", result);
    }

    [Fact]
    public async Task A_slot_without_a_year_is_the_next_one()
    {
        var (_, goals) = Setup(); // today is 1 September 2026

        var march = await new CreateGoalTool(goals).ExecuteAsync(Json("""{"title":"Spring","periodType":"month","month":3}"""));
        var q4 = await new CreateGoalTool(goals).ExecuteAsync(Json("""{"title":"Autumn","periodType":"quarter","quarter":"Q4"}"""));

        Assert.Contains("Mar 2027", march);
        Assert.Contains("Q4 2026", q4);
    }

    [Theory]
    [InlineData("""{"title":"Run 10k","periodType":"month","month":"November","year":2026,"parentKey":"GOAL-2"}""", "Create goal “Run 10k” — Monthly · under GOAL-2 · Nov 2026")]
    [InlineData("""{"title":"Run 10k","periodType":"month","periodStart":"2026-11-01","year":2026}""", "Create goal “Run 10k” — Monthly · from 2026-11-01 · Nov 2026")]
    public void A_goal_card_names_the_calendar_slot(string input, string card)
    {
        Assert.Equal(card, PersonaOS.Application.Ai.ProposedActionSummary.Describe("create_goal", input));
    }

    [Fact]
    public async Task Get_goals_lists_each_goal_with_its_parent_and_child_goals()
    {
        var (_, goals) = Setup();
        var year = await goals.CreateAsync(Year("Get fit"));
        await goals.CreateAsync(Quarter("Base", 4, parentId: year.Id));

        var result = await new GetGoalsTool(goals).ExecuteAsync(Json("{}"));

        Assert.Contains("\"childGoals\":[\"GOAL-2\"]", result);
        Assert.Contains("\"under\":\"GOAL-1\"", result);
    }
}
