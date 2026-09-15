using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Board;

/// <summary>
/// The sprint board's rules, driven by a fixed clock in UTC (so local time = UTC).
/// 2026-09-15 is a Tuesday; 2026-09-20 and 2026-09-27 are Sundays.
/// </summary>
public class BoardServiceTests
{
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SundayClose = new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

    private sealed record Rig(TestDbContext Db, BoardService Board, GoalService Goals, FixedTimeProvider Clock, FakePushSender Push)
    {
        public void At(DateTimeOffset when) => Clock.Now = when;
        public void At(int day, int hour, int minute = 0) => Clock.Now = new DateTimeOffset(2026, 9, day, hour, minute, 0, TimeSpan.Zero);
    }

    private static Rig Setup(DateTimeOffset now)
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(now);
        var push = new FakePushSender();
        db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = "android" });
        db.SaveChanges();
        var board = new BoardService(db, new FakeInstanceConfigService(db), push, clock, NullLogger<BoardService>.Instance);
        return new Rig(db, board, new GoalService(db), clock, push);
    }

    [Fact]
    public async Task A_new_board_starts_an_open_first_week_that_ends_on_Sunday()
    {
        var rig = Setup(Tuesday);

        var view = await rig.Board.GetBoardAsync();

        Assert.Equal(1, view.Sprint.Number);
        Assert.Equal(SprintStatuses.Active, view.Sprint.Status);
        Assert.Equal(SundayClose.UtcDateTime, view.Sprint.EndsAtUtc);
        // Nobody planned the first week, so filling it is not a scope change.
        Assert.False(view.Sprint.ScopeLocked);
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Write intro", Points: 3, Destination: "current"));
        Assert.False(task.AddedMidSprint);
        Assert.Equal(BoardColumns.Todo, task.Column);
    }

    [Fact]
    public async Task A_board_first_opened_during_Sunday_planning_waits_for_20_00()
    {
        var rig = Setup(new DateTimeOffset(2026, 9, 20, 18, 30, 0, TimeSpan.Zero));

        var view = await rig.Board.GetBoardAsync();

        Assert.Equal(SprintStatuses.Planned, view.Sprint.Status);
        Assert.True(view.InPlanningWindow);
        Assert.True(view.CanStartSprint);
        Assert.Equal(new DateTime(2026, 9, 20, 20, 0, 0), view.Sprint.StartsAtUtc);
    }

    [Fact]
    public async Task Sunday_closes_the_sprint_carries_unfinished_work_and_starts_the_next_at_20()
    {
        var rig = Setup(Tuesday);
        var unfinished = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Unfinished", Points: 3, Destination: "current"));
        var finished = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Finished", Points: 5, Destination: "current"));
        await rig.Board.MoveTaskAsync(finished.Id, new MoveTaskRequest("done"));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Planned ahead", Points: 2, Destination: "next"));

        rig.At(SundayClose);
        await rig.Board.RunCycleAsync();

        var report = await rig.Board.GetReportAsync();
        var closed = Assert.Single(report.Sprints);
        Assert.Equal(SprintStatuses.Closed, closed.Status);
        Assert.Equal(8, closed.CommittedPoints); // the open first week commits what it held
        Assert.Equal(5, closed.CompletedPoints);
        Assert.Equal(3, closed.CarriedOverPoints);
        Assert.Equal(5.0, report.Velocity);

        var carried = (await rig.Board.GetTaskAsync(unfinished.Id))!;
        Assert.Equal(2, carried.SprintNumber);
        Assert.Equal(1, carried.CarryOverCount);
        Assert.Equal(BoardColumns.Todo, carried.Column);
        Assert.Equal(1, (await rig.Board.GetTaskAsync(finished.Id))!.SprintNumber); // done work stays behind

        // The planning window: sprint 2 is planned, carried work first.
        var planning = await rig.Board.GetBoardAsync();
        Assert.Equal(SprintStatuses.Planned, planning.Sprint.Status);
        Assert.Equal(["Unfinished", "Planned ahead"], planning.Todo.Select(t => t.Title));

        rig.At(20, 20);
        var running = await rig.Board.GetBoardAsync();
        Assert.Equal(2, running.Sprint.Number);
        Assert.Equal(SprintStatuses.Active, running.Sprint.Status);
        Assert.Equal(5, running.Sprint.CommittedPoints);
        Assert.True(running.Sprint.ScopeLocked);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 0, 0), running.Sprint.EndsAtUtc);
        Assert.True(await rig.Db.Sprints.AnyAsync(s => s.Number == 3 && s.Status == SprintStatuses.Planned));
    }

    [Fact]
    public async Task Adding_to_a_started_sprint_needs_acknowledgement_and_is_reported_as_added()
    {
        var rig = await RunningPlannedSprintAsync();

        var ex = await Assert.ThrowsAsync<ScopeChangeException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Urgent bill", Points: 2, Destination: "current")));
        Assert.Contains("scope change", ex.Message);
        Assert.Equal("scope_change_unacknowledged", ex.Code);

        var added = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Urgent bill", Points: 2, Destination: "current", AcknowledgeScopeChange: true));
        Assert.True(added.AddedMidSprint);

        var view = await rig.Board.GetBoardAsync();
        Assert.Equal(3, view.Sprint.CommittedPoints); // unchanged by the addition
        Assert.Equal(2, view.Sprint.AddedPoints);
    }

    [Fact]
    public async Task Planning_next_week_never_warns()
    {
        var rig = await RunningPlannedSprintAsync();

        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Next week", Points: 8, Destination: "next"));
        var backlog = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Someday"));
        var moved = await rig.Board.MoveTaskAsync(backlog.Id, new MoveTaskRequest("todo", Sprint: "next"));

        Assert.False(task.AddedMidSprint);
        Assert.False(moved!.AddedMidSprint);
        var next = await rig.Board.GetBoardAsync("next");
        Assert.Equal(2, next.Todo.Count);
        Assert.False(next.Sprint.ScopeLocked);
    }

    [Fact]
    public async Task A_sprint_that_has_not_started_only_holds_this_weeks_work()
    {
        var rig = await RunningPlannedSprintAsync();
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Next week", Destination: "next"));

        await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest("in_progress", Sprint: "next")));
    }

    [Fact]
    public async Task Taking_committed_work_out_counts_as_removed_but_taking_back_an_addition_does_not()
    {
        var rig = await RunningPlannedSprintAsync();
        var committed = (await rig.Board.GetBoardAsync()).Todo.Single();

        await Assert.ThrowsAsync<ScopeChangeException>(() =>
            rig.Board.MoveTaskAsync(committed.Id, new MoveTaskRequest("backlog")));
        await rig.Board.MoveTaskAsync(committed.Id, new MoveTaskRequest("backlog", AcknowledgeScopeChange: true));

        var extra = await rig.Board.CreateTaskAsync(
            new CreateTaskRequest("Extra", Points: 5, Destination: "current", AcknowledgeScopeChange: true));
        await rig.Board.MoveTaskAsync(extra.Id, new MoveTaskRequest("backlog", AcknowledgeScopeChange: true));

        var view = await rig.Board.GetBoardAsync();
        Assert.Equal(3, view.Sprint.RemovedPoints);
        Assert.Equal(0, view.Sprint.AddedPoints);
        Assert.False((await rig.Board.GetTaskAsync(extra.Id))!.AddedMidSprint);
    }

    [Fact]
    public async Task Moving_within_a_running_sprint_is_not_a_scope_change()
    {
        var rig = await RunningPlannedSprintAsync();
        var task = (await rig.Board.GetBoardAsync()).Todo.Single();

        var started = await rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest("in_progress"));
        var done = await rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest("done"));

        Assert.Equal(BoardColumns.InProgress, started!.Column);
        Assert.NotNull(done!.CompletedAtUtc);
        Assert.Equal(3, (await rig.Board.GetBoardAsync()).Sprint.CompletedPoints);
    }

    [Fact]
    public async Task Moving_to_an_index_reorders_the_column()
    {
        var rig = Setup(Tuesday);
        var a = await rig.Board.CreateTaskAsync(new CreateTaskRequest("A"));
        var b = await rig.Board.CreateTaskAsync(new CreateTaskRequest("B"));
        var c = await rig.Board.CreateTaskAsync(new CreateTaskRequest("C"));

        await rig.Board.MoveTaskAsync(c.Id, new MoveTaskRequest("backlog", Index: 0));

        Assert.Equal(["C", "A", "B"], (await rig.Board.GetBoardAsync()).Backlog.Select(t => t.Title));
        _ = (a, b);
    }

    [Fact]
    public async Task A_deleted_tasks_number_goes_to_the_next_new_task()
    {
        var rig = Setup(Tuesday);
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("One"));
        var two = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Two"));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Three"));

        Assert.Equal("TASK-2", two.Key);
        await rig.Board.DeleteTaskAsync(two.Id);
        var next = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Four"));

        Assert.Equal("TASK-2", next.Key);
        Assert.Equal(next.Id, await rig.Board.ResolveTaskKeyAsync("task 2"));
    }

    [Fact]
    public async Task Points_must_be_Fibonacci()
    {
        var rig = Setup(Tuesday);

        await Assert.ThrowsAsync<BoardValidationException>(() =>
            rig.Board.CreateTaskAsync(new CreateTaskRequest("Odd size", Points: 4)));
    }

    [Fact]
    public async Task The_planning_nudge_goes_out_once_at_19_with_the_review()
    {
        var rig = Setup(Tuesday);
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Done", Points: 5, Destination: "current"));
        await rig.Board.MoveTaskAsync(task.Id, new MoveTaskRequest("done"));

        rig.At(20, 18, 30);
        await rig.Board.RunCycleAsync();
        Assert.Equal(0, rig.Push.SendCount); // not before 19:00

        rig.At(20, 19, 5);
        await rig.Board.RunCycleAsync();
        await rig.Board.RunCycleAsync();

        Assert.Equal(1, rig.Push.SendCount);
        Assert.Contains("Sprint 1 review: 5 of 5 points done", rig.Push.SentBodies.Single());
        Assert.Contains("plan sprint 2", rig.Push.SentBodies.Single());
    }

    [Fact]
    public async Task Starting_early_during_planning_freezes_the_commitment_and_keeps_the_end()
    {
        var rig = Setup(Tuesday);
        rig.At(SundayClose);
        await rig.Board.RunCycleAsync();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Plan", Points: 8, Destination: "current"));

        rig.At(20, 19, 30);
        var started = await rig.Board.StartSprintAsync();

        Assert.Equal(SprintStatuses.Active, started.Status);
        Assert.Equal(8, started.CommittedPoints);
        Assert.Equal(new DateTime(2026, 9, 20, 19, 30, 0), started.StartsAtUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 0, 0), started.EndsAtUtc);
        await Assert.ThrowsAsync<BoardValidationException>(() => rig.Board.StartSprintAsync());
    }

    [Fact]
    public async Task After_weeks_offline_the_cycle_catches_up()
    {
        var rig = Setup(Tuesday);
        var task = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Lingering", Points: 3, Destination: "current"));

        rig.At(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)); // Wednesday, three closes later
        var view = await rig.Board.GetBoardAsync();

        Assert.Equal(4, view.Sprint.Number);
        Assert.Equal(SprintStatuses.Active, view.Sprint.Status);
        var lingering = (await rig.Board.GetTaskAsync(task.Id))!;
        Assert.Equal(4, lingering.SprintNumber);
        Assert.Equal(3, lingering.CarryOverCount);
        Assert.Equal(0, rig.Push.SendCount); // stale nudges are not sent late
    }

    [Fact]
    public async Task Goal_progress_follows_its_tasks_and_deleting_the_goal_keeps_them()
    {
        var rig = Setup(Tuesday);
        var goal = await rig.Goals.CreateAsync(new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));
        var done = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Ownership", Points: 3, GoalId: goal.Id, Destination: "current"));
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Lifetimes", Points: 5, GoalId: goal.Id));
        await rig.Board.MoveTaskAsync(done.Id, new MoveTaskRequest("done"));

        var dto = (await rig.Goals.GetAsync(goal.Id))!;
        Assert.Equal("GOAL-1", dto.Key);
        Assert.Equal(38, dto.EffectiveProgress); // 3 of 8 points
        Assert.Equal(["TASK-1", "TASK-2"], dto.Tasks.Select(t => t.Key));
        Assert.Equal("GOAL-1", (await rig.Board.GetTaskAsync(done.Id))!.GoalKey);

        await rig.Goals.DeleteAsync(goal.Id);

        Assert.Equal(2, await rig.Db.BoardTasks.CountAsync());
        Assert.Null((await rig.Board.GetTaskAsync(done.Id))!.GoalId);
    }

    /// <summary>Sprint 2 running since Sunday 20:00 with one committed 3-point task.</summary>
    private static async Task<Rig> RunningPlannedSprintAsync()
    {
        var rig = Setup(Tuesday);
        rig.At(SundayClose);
        await rig.Board.RunCycleAsync();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Committed", Points: 3, Destination: "current"));
        rig.At(20, 20, 5);
        await rig.Board.RunCycleAsync();
        return rig;
    }
}
