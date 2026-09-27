using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;
using static PersonaOS.Tests.TestSupport.TestGoals;

namespace PersonaOS.Tests.Board;

/// <summary>Tasks sit only under open monthly goals (owner's rules, 2026-09-27).</summary>
public class TaskGoalRuleTests
{
    private static (BoardService Board, GoalService Goals) Setup()
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(Today);
        var board = new BoardService(db, new FakeInstanceConfigService(db), new FakePushSender(), clock, NullLogger<BoardService>.Instance);
        return (board, Service(db, clock));
    }

    [Fact]
    public async Task A_task_cannot_go_under_a_year_or_quarter_goal()
    {
        var (board, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        var month = await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));

        var ex = await Assert.ThrowsAsync<BoardValidationException>(() =>
            board.CreateTaskAsync(new CreateTaskRequest("Run", GoalId: quarter.Id)));
        Assert.Contains("only under monthly goals", ex.Message);

        var task = await board.CreateTaskAsync(new CreateTaskRequest("Run", GoalId: month.Id));
        Assert.Equal(month.Key, task.GoalKey);
    }

    [Fact]
    public async Task A_completed_goal_takes_no_new_tasks_and_keeps_its_done_ones_done()
    {
        var (board, goals) = Setup();
        var month = await goals.CreateAsync(Month("Run"));
        var task = await board.CreateTaskAsync(new CreateTaskRequest("5k", GoalId: month.Id));
        await board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.Backlog));
        var sprint = await board.CreateSprintAsync(new CreateSprintRequest("Week"));
        await board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.Todo, sprint.Key));
        await board.StartSprintAsync(sprint.Id);
        await board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.Done));
        await goals.UpdateStatusAsync(month.Id, GoalStatuses.Completed);

        await Assert.ThrowsAsync<BoardValidationException>(() =>
            board.CreateTaskAsync(new CreateTaskRequest("More", GoalId: month.Id)));
        var reopen = await Assert.ThrowsAsync<BoardValidationException>(() =>
            board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.InProgress)));
        Assert.Contains("reopen it before reopening TASK-1", reopen.Message);
    }
}
