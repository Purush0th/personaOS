using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Board;
using PersonaOS.Application.Board.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Application.Planner;
using PersonaOS.Application.Planner.Tools;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Board;

/// <summary>
/// The assistant addresses goals and tasks by key. A model on a phone passed a goal's position in a
/// numbered list ("Goal 3 does not exist") — keys make that mistake either impossible or loud.
/// </summary>
public class BoardToolsTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (TestDbContext Db, BoardService Board, GoalService Goals) Setup()
    {
        var db = TestDbContext.Create();
        var board = new BoardService(db, new FakeInstanceConfigService(db), new FakePushSender(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero)), NullLogger<BoardService>.Instance);
        return (db, board, new GoalService(db));
    }

    [Fact]
    public async Task Goal_tools_find_the_goal_by_key_not_by_position()
    {
        var (_, _, goals) = Setup();
        await goals.CreateAsync(new CreateGoalRequest("Get masters", null, GoalPeriods.Year, new DateOnly(2026, 1, 1)));
        await goals.CreateAsync(new CreateGoalRequest("Read books", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));

        var result = await new UpdateGoalStatusTool(goals).ExecuteAsync(Json("""{"goalKey":"GOAL-2","progress":20}"""));

        Assert.Contains("Read books", result);
        var goal = (await goals.GetAllAsync()).Single(g => g.Key == "GOAL-2");
        Assert.Equal(20, goal.Progress);
    }

    [Fact]
    public async Task A_key_that_does_not_exist_says_to_use_keys_not_positions()
    {
        var (_, _, goals) = Setup();

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() =>
            new UpdateGoalStatusTool(goals).ExecuteAsync(Json("""{"goalKey":"GOAL-3","status":"dropped"}""")));

        Assert.Contains("not list positions", ex.Message);
    }

    [Fact]
    public async Task Create_task_puts_it_under_the_goal_with_points()
    {
        var (_, board, goals) = Setup();
        await goals.CreateAsync(new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));

        var result = await new CreateTaskTool(board, goals).ExecuteAsync(
            Json("""{"title":"Borrow checker","goalKey":"GOAL-1","points":5,"destination":"current"}"""));

        var task = (await board.GetBoardAsync()).Todo.Single();
        Assert.Equal("TASK-1", task.Key);
        Assert.Equal("GOAL-1", task.GoalKey);
        Assert.Equal(5, task.Points);
        Assert.Contains("TASK-1", result);
    }

    [Fact]
    public async Task Move_and_update_tools_take_task_keys()
    {
        var (_, board, goals) = Setup();
        var task = await board.CreateTaskAsync(new CreateTaskRequest("Write intro", Destination: "current"));

        await new UpdateTaskTool(board, goals).ExecuteAsync(Json("""{"taskKey":"TASK-1","points":3}"""));
        await new MoveTaskTool(board, goals).ExecuteAsync(Json("""{"taskKey":"task-1","column":"in_progress"}"""));

        var moved = (await board.GetTaskAsync(task.Id))!;
        Assert.Equal(3, moved.Points);
        Assert.Equal(BoardColumns.InProgress, moved.Column);
    }

    [Fact]
    public async Task A_day_plan_can_be_picked_from_a_board_task_and_shows_its_goal()
    {
        var (db, board, goals) = Setup();
        await goals.CreateAsync(new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));
        await board.CreateTaskAsync(new CreateTaskRequest("Borrow checker", GoalId: 1, Destination: "current"));
        var planner = new PlannerService(db);

        await new AddPlannerItemTool(planner, goals, board).ExecuteAsync(
            Json("""{"taskKey":"TASK-1","date":"2026-09-15"}"""));

        var item = (await planner.GetDayAsync(new DateOnly(2026, 9, 15))).Items.Single();
        Assert.Equal("Borrow checker", item.Title);
        Assert.Equal("TASK-1", item.TaskKey);
        Assert.Equal("GOAL-1", item.GoalKey);
        Assert.Equal("Learn Rust", item.GoalTitle);
    }

    [Fact]
    public void Proposal_cards_name_the_task_and_warn_about_scope()
    {
        var summary = ProposedActionSummary.Describe("create_task", """{"title":"Pay bill","points":2,"destination":"current"}""");

        Assert.Contains("“Pay bill”", summary);
        Assert.Contains("points 2", summary);
        Assert.Contains("no goal", summary);
        Assert.Contains("scope change", summary);

        var move = ProposedActionSummary.Describe("move_task", """{"taskKey":"TASK-4","column":"done"}""");
        Assert.Contains("TASK-4", move);
        Assert.DoesNotContain("scope change", move);
    }
}
