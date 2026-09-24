using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Board;

/// <summary>
/// The sprint board's rules, driven by a fixed clock in UTC (so local time = UTC).
/// Sprints are created, started and completed by hand — nothing happens on a timer except the
/// Sunday nudge. 2026-09-15 is a Tuesday; 2026-09-20 and 2026-09-27 are Sundays.
/// </summary>
public class BoardServiceTests
{
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    private sealed record Rig(
        TestDbContext Db, BoardService Board, GoalService Goals, FixedTimeProvider Clock, FakePushSender Push)
    {
        public void At(int day, int hour, int minute = 0) =>
            Clock.Now = new DateTimeOffset(2026, 9, day, hour, minute, 0, TimeSpan.Zero);
    }

    private static Rig Setup(DateTimeOffset? now = null)
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(now ?? Tuesday);
        var push = new FakePushSender();
        db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = "android" });
        db.SaveChanges();
        var board = new BoardService(db, new FakeInstanceConfigService(db), push, clock, NullLogger<BoardService>.Instance);
        return new Rig(db, board, new GoalService(db), clock, push);
    }

    /// <summary>A rig with SPRINT-1 running and one committed 3-point task in it.</summary>
    private static async Task<(Rig Rig, SprintDto Sprint, BoardTaskDto Task)> RunningSprintAsync()
    {
        var rig = Setup();
        var sprint = await rig.Board.CreateSprintAsync(new CreateSprintRequest("Week one"));
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Committed", Points: 3, SprintKey: sprint.Key));
        var started = await rig.Board.StartSprintAsync(sprint.Id);
        return (rig, started!, task);
    }

    [Fact]
    public async Task A_new_board_has_no_sprint_and_only_a_backlog()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Someday"));

        var board = await rig.Board.GetBoardAsync();
        var plan = await rig.Board.GetPlanAsync();

        Assert.Null(board.Sprint);
        Assert.Empty(board.Todo);
        Assert.Empty(plan.Sprints);
        Assert.Equal(["TASK-1"], plan.Backlog.Select(t => t.Key));
    }

    [Fact]
    public async Task A_created_sprint_gets_a_key_a_name_and_the_next_free_week()
    {
        var rig = Setup();

        var first = await rig.Board.CreateSprintAsync(new CreateSprintRequest("Paperwork week"));
        var second = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));

        Assert.Equal("SPRINT-1", first.Key);
        Assert.Equal("Paperwork week", first.Name);
        Assert.Equal(SprintStatuses.Planned, first.Status);
        // Defaults follow the Sunday 20:00 → Sunday 18:00 rhythm, and never overlap.
        // The first one starts now and runs to the coming Sunday; the next takes the week after.
        Assert.Equal(new DateTime(2026, 9, 15, 9, 0, 0), first.StartsAtUtc);
        Assert.Equal(new DateTime(2026, 9, 20, 18, 0, 0), first.EndsAtUtc);
        Assert.Equal("SPRINT-2", second.Key);
        Assert.Equal(new DateTime(2026, 9, 20, 20, 0, 0), second.StartsAtUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 0, 0), second.EndsAtUtc);
    }

    [Fact]
    public async Task Dates_can_be_set_and_must_make_sense()
    {
        var rig = Setup();
        var sprint = await rig.Board.CreateSprintAsync(new CreateSprintRequest(
            "Holiday", new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 10, 17, 0, 0)));

        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), sprint.StartsAtUtc);
        Assert.Equal(new DateTime(2026, 10, 10, 17, 0, 0), sprint.EndsAtUtc);

        var renamed = await rig.Board.UpdateSprintAsync(sprint.Id, new UpdateSprintRequest(Name: "Trip"));
        Assert.Equal("Trip", renamed!.Name);

        await Assert.ThrowsAsync<BoardValidationException>(() => rig.Board.UpdateSprintAsync(
            sprint.Id, new UpdateSprintRequest(EndsAtLocal: new DateTime(2026, 9, 30, 9, 0, 0))));
    }

    [Fact]
    public async Task Nothing_starts_by_itself_and_only_one_sprint_runs()
    {
        var rig = Setup();
        var first = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        var second = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Planned", Points: 5, SprintKey: first.Key));

        // The start date passes; the sprint is still only planned.
        rig.At(21, 10);
        Assert.Null((await rig.Board.GetBoardAsync()).Sprint);

        var started = await rig.Board.StartSprintAsync(first.Id);
        Assert.Equal(SprintStatuses.Active, started!.Status);
        Assert.Equal(5, started.CommittedPoints);
        Assert.True(started.ScopeLocked);

        var ex = await Assert.ThrowsAsync<BoardValidationException>(() => rig.Board.StartSprintAsync(second.Id));
        Assert.Contains("SPRINT-1 is still running", ex.Message);
    }

    [Fact]
    public async Task The_board_shows_the_running_sprint_without_a_backlog_column()
    {
        var (rig, sprint, task) = await RunningSprintAsync();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Not this week"));
        await rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.InProgress));

        var board = await rig.Board.GetBoardAsync();

        Assert.Equal(sprint.Key, board.Sprint!.Key);
        Assert.Equal(["TASK-1"], board.InProgress.Select(t => t.Key));
        Assert.Empty(board.Todo);
        // The backlog lives on the plan page now.
        Assert.Equal(["TASK-2"], (await rig.Board.GetPlanAsync()).Backlog.Select(t => t.Key));
    }

    [Fact]
    public async Task Adding_to_a_running_sprint_needs_acknowledgement_and_is_reported_as_added()
    {
        var (rig, sprint, _) = await RunningSprintAsync();

        var ex = await Assert.ThrowsAsync<ScopeChangeException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Urgent bill", Points: 2, SprintKey: sprint.Key)));
        Assert.Equal("scope_change_unacknowledged", ex.Code);
        Assert.Contains("SPRINT-1 is running", ex.Message);

        var added = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Urgent bill", Points: 2, SprintKey: sprint.Key, AcknowledgeScopeChange: true));

        Assert.True(added.AddedMidSprint);
        var board = await rig.Board.GetBoardAsync();
        Assert.Equal(3, board.Sprint!.CommittedPoints); // unchanged by the addition
        Assert.Equal(2, board.Sprint.AddedPoints);
    }

    [Fact]
    public async Task A_task_moves_from_one_sprint_to_another_by_key()
    {
        var (rig, running, task) = await RunningSprintAsync();
        var next = await rig.Board.CreateSprintAsync(new CreateSprintRequest("Week two"));

        await Assert.ThrowsAsync<ScopeChangeException>(() =>
            rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.Todo, next.Key)));
        var moved = await rig.Board.MoveTaskAsync(
            task.Id, new MoveTaskRequest(BoardColumns.Todo, next.Key, AcknowledgeScopeChange: true));

        Assert.Equal(next.Key, moved!.SprintKey);
        Assert.Equal("Week two", moved.SprintName);
        // Leaving a running sprint is reported as removed scope.
        var board = await rig.Board.GetBoardAsync();
        Assert.Equal(running.Key, board.Sprint!.Key);
        Assert.Equal(3, board.Sprint.RemovedPoints);
        Assert.Empty(board.Todo);
    }

    [Fact]
    public async Task Work_cannot_be_in_progress_in_a_sprint_that_has_not_started()
    {
        var rig = Setup();
        var planned = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Later", SprintKey: planned.Key));

        var ex = await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.InProgress, planned.Key)));

        Assert.Contains("has not started", ex.Message);
    }

    [Theory]
    [InlineData(BoardColumns.InProgress)]
    [InlineData(BoardColumns.Done)]
    public async Task A_task_can_be_created_straight_into_a_running_sprints_column(string column)
    {
        var (rig, sprint, existing) = await RunningSprintAsync();

        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest(
            "Already started", SprintKey: sprint.Key, AcknowledgeScopeChange: true, Column: column));

        Assert.Equal(column, task.Column);
        Assert.True(task.AddedMidSprint);
        Assert.Equal(column == BoardColumns.Done, task.CompletedAtUtc is not null);
        var board = await rig.Board.GetBoardAsync();
        var landed = column == BoardColumns.Done ? board.Done : board.InProgress;
        Assert.Equal([task.Key], landed.Select(t => t.Key));
        Assert.Equal([existing.Key], board.Todo.Select(t => t.Key));
    }

    [Fact]
    public async Task Creating_without_a_column_still_lands_in_to_do()
    {
        var (rig, sprint, _) = await RunningSprintAsync();

        var task = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Next up", SprintKey: sprint.Key, AcknowledgeScopeChange: true));

        Assert.Equal(BoardColumns.Todo, task.Column);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public async Task Created_work_is_only_in_progress_inside_a_running_sprint()
    {
        var rig = Setup();
        var planned = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));

        var inBacklog = await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Loose", Column: BoardColumns.InProgress)));
        var inPlanned = await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Later", SprintKey: planned.Key, Column: BoardColumns.Done)));
        var unknown = await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Odd", Column: "blocked")));

        Assert.Contains("in a sprint", inBacklog.Message);
        Assert.Contains("has not started", inPlanned.Message);
        Assert.Contains("Column must be one of", unknown.Message);
        Assert.Empty((await rig.Board.GetPlanAsync()).Backlog);
    }

    [Fact]
    public async Task Completing_a_sprint_freezes_it_and_carries_unfinished_work_to_the_next_one()
    {
        var (rig, sprint, unfinished) = await RunningSprintAsync();
        var done = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Finished", Points: 5, SprintKey: sprint.Key, AcknowledgeScopeChange: true));
        await rig.Board.MoveTaskAsync(done.Id, new MoveTaskRequest(BoardColumns.Done));
        var next = await rig.Board.CreateSprintAsync(new CreateSprintRequest("Week two"));

        var closed = await rig.Board.CompleteSprintAsync(sprint.Id, new CompleteSprintRequest());

        Assert.Equal(SprintStatuses.Closed, closed!.Status);
        Assert.Equal(3, closed.CommittedPoints);
        Assert.Equal(5, closed.CompletedPoints);
        Assert.Equal(5, closed.AddedPoints);
        Assert.Equal(3, closed.CarriedOverPoints);

        var carried = (await rig.Board.GetTaskAsync(unfinished.Id))!.Task;
        Assert.Equal(next.Key, carried.SprintKey);
        Assert.Equal(1, carried.CarryOverCount);
        // Finished work stays behind in the sprint that finished it.
        Assert.Equal(sprint.Key, (await rig.Board.GetTaskAsync(done.Id))!.Task.SprintKey);

        Assert.Null((await rig.Board.GetBoardAsync()).Sprint); // nothing runs until the user starts one
        Assert.Equal(5.0, (await rig.Board.GetReportAsync()).Velocity);
    }

    [Fact]
    public async Task Unfinished_work_can_go_back_to_the_backlog_instead()
    {
        var (rig, sprint, task) = await RunningSprintAsync();

        await rig.Board.CompleteSprintAsync(sprint.Id, new CompleteSprintRequest(ToBacklog: true));

        var plan = await rig.Board.GetPlanAsync();
        Assert.Equal([task.Key], plan.Backlog.Select(t => t.Key));
        Assert.Empty(plan.Sprints);
    }

    [Fact]
    public async Task A_planned_sprint_can_be_deleted_and_its_work_returns_to_the_backlog()
    {
        var rig = Setup();
        var planned = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Later", SprintKey: planned.Key));

        Assert.True(await rig.Board.DeleteSprintAsync(planned.Id));
        Assert.Equal(["TASK-1"], (await rig.Board.GetPlanAsync()).Backlog.Select(t => t.Key));

        var (running, sprint, task) = await RunningSprintAsync();
        var ex = await Assert.ThrowsAsync<BoardValidationException>(() => running.Board.DeleteSprintAsync(sprint.Id));
        Assert.Contains("complete it instead", ex.Message);

        // Emptied, it records nothing, and refusing to remove it left sprint history growing
        // forever — a test run or a mistake could never be cleaned up.
        await running.Board.DeleteTaskAsync(task.Id);
        Assert.True(await running.Board.DeleteSprintAsync(sprint.Id));
        Assert.Empty((await running.Board.GetPlanAsync()).Sprints);
    }

    [Fact]
    public async Task A_deleted_key_number_goes_to_the_next_new_item()
    {
        var rig = Setup();
        var first = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        var second = await rig.Board.CreateSprintAsync(new CreateSprintRequest(null));
        Assert.Equal("SPRINT-2", second.Key);

        // Deleting a planned sprint frees its number, the same way task numbers work.
        await rig.Board.DeleteSprintAsync(first.Id);
        Assert.Equal("SPRINT-1", (await rig.Board.CreateSprintAsync(new CreateSprintRequest(null))).Key);

        await rig.Board.CreateTaskAsync(new CreateTaskRequest("One"));
        var two = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Two"));
        await rig.Board.DeleteTaskAsync(two.Id);
        Assert.Equal("TASK-2", (await rig.Board.CreateTaskAsync(new CreateTaskRequest("Three"))).Key);
    }

    [Fact]
    public async Task Points_are_Fibonacci_and_priority_is_one_of_five()
    {
        var rig = Setup();

        await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Odd size", Points: 4)));
        await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Odd urgency", Priority: "urgent")));

        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Fine", Points: 8, Priority: "HIGH"));
        Assert.Equal(WorkItemPriorities.High, task.Priority);
        Assert.Equal(WorkItemPriorities.Medium, (await rig.Board.CreateTaskAsync(new CreateTaskRequest("Plain"))).Priority);
    }

    [Fact]
    public async Task The_sprint_view_shows_the_points_coming_down_day_by_day()
    {
        var (rig, sprint, first) = await RunningSprintAsync();
        var second = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Second", Points: 5, SprintKey: sprint.Key, AcknowledgeScopeChange: true));

        await rig.Board.MoveTaskAsync(first.Id, new MoveTaskRequest(BoardColumns.Done));
        rig.At(17, 12);
        await rig.Board.MoveTaskAsync(second.Id, new MoveTaskRequest(BoardColumns.Done));

        var detail = await rig.Board.GetSprintAsync(sprint.Id);

        Assert.Equal(3, detail!.Burndown.Count); // 15th, 16th, 17th
        Assert.Equal(new DateOnly(2026, 9, 15), detail.Burndown[0].Date);
        Assert.Equal(5, detail.Burndown[0].RemainingPoints); // 3 of 8 done on day one
        Assert.Equal(5, detail.Burndown[1].RemainingPoints); // nothing finished on day two
        Assert.Equal(0, detail.Burndown[2].RemainingPoints);
        Assert.Equal(8, detail.Burndown[2].CompletedPoints);
    }

    [Fact]
    public async Task A_closed_sprint_chart_still_shows_the_work_that_was_carried_out_of_it()
    {
        // The carried tasks are in the next sprint by then, so the chart has to come from the
        // sprint's own frozen totals — otherwise it looks like the week only ever held what it
        // finished, and the line always lands on zero.
        var (rig, sprint, unfinished) = await RunningSprintAsync();
        var done = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Finished", Points: 5, SprintKey: sprint.Key, AcknowledgeScopeChange: true));
        await rig.Board.MoveTaskAsync(done.Id, new MoveTaskRequest(BoardColumns.Done));
        await rig.Board.CompleteSprintAsync(sprint.Id, new CompleteSprintRequest(ToBacklog: true));

        var detail = await rig.Board.GetSprintAsync(sprint.Id);

        Assert.Equal(8, detail!.Burndown[0].RemainingPoints + detail.Burndown[0].CompletedPoints);
        Assert.Equal(3, detail.Burndown[^1].RemainingPoints); // the carried task never got done
        Assert.Equal(5, detail.Burndown[^1].CompletedPoints);
        _ = unfinished;
    }

    [Fact]
    public async Task The_Sunday_nudge_goes_out_once_and_says_where_the_sprint_stands()
    {
        var (rig, sprint, task) = await RunningSprintAsync();
        await rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest(BoardColumns.Done));

        rig.At(20, 18, 30);
        Assert.Empty(await rig.Board.RunRemindersAsync()); // not before 19:00

        rig.At(20, 19, 5);
        await rig.Board.RunRemindersAsync();
        await rig.Board.RunRemindersAsync();

        Assert.Equal(1, rig.Push.SendCount);
        // The sprint ran out on Sunday at 18:00, so the nudge says so and asks for the next one.
        var body = rig.Push.SentBodies.Single();
        Assert.Contains("SPRINT-1 “Week one” has reached its end date: 3 of 3 points done", body);
        Assert.Contains("plan the next one", body);
    }

    [Fact]
    public async Task With_nothing_running_the_nudge_asks_for_a_plan()
    {
        var rig = Setup();
        rig.At(20, 19, 5);

        await rig.Board.RunRemindersAsync();

        Assert.Contains("No sprint is running", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task Work_finished_outside_a_sprint_reads_as_done_not_as_backlog()
    {
        // How a completed sub-goal arrives after the migration: done, but in no sprint. Calling it
        // "Backlog" made finished work look unstarted, and it is not on the board to correct there.
        var rig = Setup();
        var goal = await rig.Goals.CreateAsync(new CreateGoalRequest("Read books", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Buy a book", GoalId: goal.Id));
        rig.Db.BoardTasks.Single(t => t.Id == task.Id).Status = BoardTaskStatuses.Done;
        await rig.Db.SaveChangesAsync();

        Assert.Equal(BoardColumns.Done, (await rig.Board.GetTaskAsync(task.Id))!.Task.Column);
        Assert.Equal(BoardColumns.Done, (await rig.Goals.GetAsync(goal.Id))!.Tasks.Single().Column);
        // It stays off the plan's backlog — that is work still to do — so the goal is where it is managed.
        Assert.Empty((await rig.Board.GetPlanAsync()).Backlog);

        Assert.True(await rig.Board.DeleteTaskAsync(task.Id));
        Assert.Empty((await rig.Goals.GetAsync(goal.Id))!.Tasks);
    }

    [Fact]
    public async Task Goal_progress_follows_its_tasks_and_deleting_the_goal_keeps_them()
    {
        var rig = Setup();
        var goal = await rig.Goals.CreateAsync(new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));
        var done = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Ownership", Points: 3, GoalId: goal.Id));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Lifetimes", Points: 5, GoalId: goal.Id));
        rig.Db.BoardTasks.Single(t => t.Id == done.Id).Status = BoardTaskStatuses.Done;
        await rig.Db.SaveChangesAsync();

        var dto = (await rig.Goals.GetAsync(goal.Id))!;
        Assert.Equal("GOAL-1", dto.Key);
        Assert.Equal(38, dto.EffectiveProgress); // 3 of 8 points
        Assert.Equal(["TASK-1", "TASK-2"], dto.Tasks.Select(t => t.Key));

        await rig.Goals.DeleteAsync(goal.Id);

        Assert.Equal(2, await rig.Db.BoardTasks.CountAsync());
        Assert.Null((await rig.Board.GetTaskAsync(done.Id))!.Task.GoalId);
    }
}
