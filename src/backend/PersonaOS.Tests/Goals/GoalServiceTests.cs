using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;
using static PersonaOS.Tests.TestSupport.TestGoals;

namespace PersonaOS.Tests.Goals;

/// <summary>
/// Goals and their year → quarter → month hierarchy, on a fixed today of 1 September 2026. The
/// service is where the rules hold for every caller — web, phone, API and assistant.
/// </summary>
public class GoalServiceTests
{
    private static (TestDbContext Db, GoalService Goals) Setup()
    {
        var db = TestDbContext.Create();
        return (db, Service(db));
    }

    private static async Task<BoardTask> TaskUnder(TestDbContext db, int goalId, int number, bool done = false)
    {
        var task = new BoardTask
        {
            Number = number, Title = $"Task {number}", GoalId = goalId,
            Status = done ? BoardTaskStatuses.Done : BoardTaskStatuses.Todo,
        };
        db.BoardTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    // ---------------------------------------------------------------- keys

    [Fact]
    public async Task Goals_get_sequential_keys()
    {
        var (_, goals) = Setup();

        var first = await goals.CreateAsync(Month("Run"));
        var second = await goals.CreateAsync(Month("Read", 10));

        Assert.Equal("GOAL-1", first.Key);
        Assert.Equal("GOAL-2", second.Key);
    }

    [Theory]
    [InlineData("GOAL-2")]
    [InlineData("goal-2")]
    [InlineData("2")]
    [InlineData("#2")]
    public async Task A_key_resolves_however_the_model_writes_it(string key)
    {
        var (_, goals) = Setup();
        await goals.CreateAsync(Month("Run"));
        var read = await goals.CreateAsync(Month("Read"));

        Assert.Equal(read.Id, await goals.ResolveKeyAsync(key));
    }

    [Theory]
    [InlineData("TASK-2")]
    [InlineData("GOAL-9")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_key_for_something_else_or_nothing_resolves_to_nothing(string? key)
    {
        var (_, goals) = Setup();
        await goals.CreateAsync(Month("Run"));
        await goals.CreateAsync(Month("Read"));

        Assert.Null(await goals.ResolveKeyAsync(key));
    }

    // ---------------------------------------------------------------- calendar

    [Fact]
    public async Task Dates_come_from_the_type_and_slot()
    {
        var (_, goals) = Setup();

        var year = await goals.CreateAsync(Year("Get fit"));
        var quarter = await goals.CreateAsync(Quarter("Build a base", 4));
        var month = await goals.CreateAsync(Month("Run 5k", 11));

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), "2026"), (year.PeriodStart, year.PeriodEnd, year.Slot));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), "Q4 2026"), (quarter.PeriodStart, quarter.PeriodEnd, quarter.Slot));
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Nov 2026"), (month.PeriodStart, month.PeriodEnd, month.Slot));
    }

    [Fact]
    public async Task A_slot_already_under_way_starts_today_and_one_too_short_is_refused()
    {
        var db = TestDbContext.Create();
        var goals = Service(db, new FixedTimeProvider(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero)));

        // Q3 has four days left on 27 September; the owner's example of what must be refused.
        var quarter = await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Quarter("Late", 3)));
        var month = await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("Late", 9)));

        Assert.Contains("at least 45", quarter.Message);
        Assert.Contains("at least 15", month.Message);
        Assert.Equal(new DateOnly(2026, 10, 1), (await goals.CreateAsync(Quarter("Next", 4))).PeriodStart);
    }

    [Fact]
    public async Task A_quarter_can_be_picked_in_a_future_year_but_not_a_past_one()
    {
        var (_, goals) = Setup();

        var later = await goals.CreateAsync(Quarter("Later", 1, 2028));

        Assert.Equal(new DateOnly(2028, 1, 1), later.PeriodStart);
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Quarter("Past", 4, 2025)));
    }

    // ---------------------------------------------------------------- nesting

    [Fact]
    public async Task Goals_nest_year_quarter_month_and_nothing_else()
    {
        var (_, goals) = Setup();
        var year = await goals.CreateAsync(Year("Get fit"));
        var quarter = await goals.CreateAsync(Quarter("Base", 4, parentId: year.Id));
        var month = await goals.CreateAsync(Month("Run", 10, parentId: quarter.Id));

        Assert.Equal(year.Key, (await goals.GetAsync(quarter.Id))!.ParentKey);
        Assert.Equal(["GOAL-3"], (await goals.GetAsync(quarter.Id))!.Children.Select(c => c.Key));

        var monthUnderYear = await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("x", 11, parentId: year.Id)));
        Assert.Contains("goes under a quarterly goal", monthUnderYear.Message);
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("x", 11, parentId: month.Id)));
        await Assert.ThrowsAsync<GoalValidationException>(() =>
            goals.CreateAsync(new CreateGoalRequest("x", null, GoalPeriods.Year, 2027, ParentId: year.Id)));
    }

    [Fact]
    public async Task A_child_takes_a_slot_of_its_parent_once()
    {
        var (_, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        await goals.CreateAsync(Month("October", 10, parentId: quarter.Id));

        var taken = await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("Again", 10, parentId: quarter.Id)));
        var outside = await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("January", 1, 2027, quarter.Id)));

        Assert.Contains("already has Oct 2026", taken.Message);
        Assert.Contains("not in Q4 2026", outside.Message);
    }

    // ---------------------------------------------------------------- progress

    [Fact]
    public async Task A_month_counts_its_tasks_and_a_quarter_averages_its_months_equally()
    {
        var (db, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        var october = await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));
        var november = await goals.CreateAsync(Month("Nov", 11, parentId: quarter.Id));
        var december = await goals.CreateAsync(Month("Dec", 12, parentId: quarter.Id));
        await TaskUnder(db, october.Id, 1, done: true);
        for (var n = 2; n <= 10; n++) await TaskUnder(db, november.Id, n);
        await goals.UpdateStatusAsync(december.Id, GoalStatuses.Completed); // no tasks, completed

        // The owner's example: 100% + 0% + 100%, each month counting once.
        Assert.Equal(67, (await goals.GetAsync(quarter.Id))!.EffectiveProgress);
        Assert.Equal(0, (await goals.GetAsync(november.Id))!.EffectiveProgress);
    }

    [Fact]
    public async Task Value_points_never_count_toward_progress()
    {
        var (db, goals) = Setup();
        var month = await goals.CreateAsync(Month("Run"));
        db.BoardTasks.Add(new BoardTask { Number = 1, Title = "Big", GoalId = month.Id, Points = 13, Status = BoardTaskStatuses.Done });
        db.BoardTasks.Add(new BoardTask { Number = 2, Title = "Small", GoalId = month.Id, Points = 1 });
        await db.SaveChangesAsync();

        Assert.Equal(50, (await goals.GetAsync(month.Id))!.EffectiveProgress);
    }

    [Fact]
    public async Task Manual_progress_is_only_for_a_goal_without_tasks_or_children()
    {
        var (db, goals) = Setup();
        var month = await goals.CreateAsync(Month("Run"));
        await goals.UpdateAsync(month.Id, new UpdateGoalRequest(Progress: 40));
        Assert.Equal(40, (await goals.GetAsync(month.Id))!.EffectiveProgress);

        await TaskUnder(db, month.Id, 1);

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateAsync(month.Id, new UpdateGoalRequest(Progress: 60)));
        Assert.Contains("takes its progress from its tasks", ex.Message);
    }

    // ---------------------------------------------------------------- completing

    [Fact]
    public async Task A_month_with_open_tasks_cannot_be_completed()
    {
        var (db, goals) = Setup();
        var month = await goals.CreateAsync(Month("Run"));
        var task = await TaskUnder(db, month.Id, 5);

        Assert.Contains("TASK-5", (await goals.GetAsync(month.Id))!.CompleteProblem);
        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateStatusAsync(month.Id, GoalStatuses.Completed));
        Assert.Contains("open tasks: TASK-5", ex.Message);

        task.Status = BoardTaskStatuses.Done;
        await db.SaveChangesAsync();
        var done = await goals.UpdateStatusAsync(month.Id, GoalStatuses.Completed);
        Assert.Equal(100, done!.EffectiveProgress);
    }

    [Fact]
    public async Task A_parent_waits_for_its_active_children_and_a_child_waits_for_its_parent_to_reopen()
    {
        var (_, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        var month = await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));

        var early = await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateStatusAsync(quarter.Id, GoalStatuses.Completed));
        Assert.Contains("child goals first: GOAL-2", early.Message);

        await goals.UpdateStatusAsync(month.Id, GoalStatuses.Completed);
        await goals.UpdateStatusAsync(quarter.Id, GoalStatuses.Completed);

        var reopen = await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateStatusAsync(month.Id, GoalStatuses.Active));
        Assert.Contains("reopen it before reopening GOAL-2", reopen.Message);
    }

    [Fact]
    public async Task There_is_no_dropped_status()
    {
        var (_, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));

        await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateStatusAsync(goal.Id, "dropped"));
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("  ")));
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateAsync(goal.Id, new UpdateGoalRequest(Progress: 101)));
    }

    // ---------------------------------------------------------------- moving

    [Fact]
    public async Task Moving_a_quarter_moves_its_months_to_the_same_places()
    {
        var (_, goals) = Setup();
        var thisYear = await goals.CreateAsync(Year("2026"));
        var nextYear = await goals.CreateAsync(Year("2027", 2027));
        var quarter = await goals.CreateAsync(Quarter("Base", 4, parentId: thisYear.Id));
        var second = await goals.CreateAsync(Month("Nov", 11, parentId: quarter.Id));

        var moved = await goals.MoveAsync(quarter.Id, new MoveGoalRequest(nextYear.Id, Quarter: 2));

        Assert.Equal(("Q2 2027", nextYear.Key), (moved!.Slot, moved.ParentKey));
        Assert.Equal("May 2027", (await goals.GetAsync(second.Id))!.Slot); // still the second month
    }

    [Fact]
    public async Task Detaching_keeps_the_dates()
    {
        var (_, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        var month = await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));

        var detached = await goals.MoveAsync(month.Id, new MoveGoalRequest(null));

        Assert.Null(detached!.ParentId);
        Assert.Equal(month.PeriodStart, detached.PeriodStart);
    }

    [Fact]
    public async Task A_nested_goal_changes_type_only_after_it_is_detached()
    {
        var (_, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        var month = await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() =>
            goals.UpdateAsync(month.Id, new UpdateGoalRequest(PeriodType: GoalPeriods.Quarter, Quarter: 4)));
        Assert.Contains("detach it first", ex.Message);
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public async Task A_goal_with_child_goals_cannot_be_deleted()
    {
        var (_, goals) = Setup();
        var quarter = await goals.CreateAsync(Quarter("Base", 4));
        await goals.CreateAsync(Month("Oct", 10, parentId: quarter.Id));

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => goals.DeleteAsync(quarter.Id, new DeleteGoalRequest()));
        Assert.Contains("Delete them first: GOAL-2", ex.Message);
    }

    [Fact]
    public async Task Deleting_keeps_its_tasks_and_planner_items_by_default()
    {
        var (db, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));
        await TaskUnder(db, goal.Id, 1);
        db.PlannerItems.Add(new PlannerItem { Title = "5k", Date = new DateOnly(2026, 9, 2), GoalId = goal.Id });
        await db.SaveChangesAsync();

        Assert.True(await goals.DeleteAsync(goal.Id, new DeleteGoalRequest()));

        Assert.Null(db.BoardTasks.Single().GoalId);
        Assert.Null(db.PlannerItems.Single().GoalId);
        Assert.Empty(db.Goals);
    }

    [Fact]
    public async Task Deleting_can_take_the_tasks_with_it()
    {
        var (db, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));
        await TaskUnder(db, goal.Id, 1);

        await goals.DeleteAsync(goal.Id, new DeleteGoalRequest(GoalTaskActions.Delete));

        Assert.Empty(db.BoardTasks);
    }

    [Fact]
    public async Task Reassigning_maps_each_task_to_its_own_monthly_goal_or_none()
    {
        var (db, goals) = Setup();
        var old = await goals.CreateAsync(Month("Old", 9));
        var october = await goals.CreateAsync(Month("Oct", 10));
        var quarter = await goals.CreateAsync(Quarter("Q", 4));
        var first = await TaskUnder(db, old.Id, 1);
        var second = await TaskUnder(db, old.Id, 2);

        var missing = await Assert.ThrowsAsync<GoalValidationException>(() => goals.DeleteAsync(old.Id,
            new DeleteGoalRequest(GoalTaskActions.Reassign, new Dictionary<string, string?> { ["TASK-1"] = october.Key })));
        var notMonthly = await Assert.ThrowsAsync<GoalValidationException>(() => goals.DeleteAsync(old.Id,
            new DeleteGoalRequest(GoalTaskActions.Reassign, new Dictionary<string, string?> { ["TASK-1"] = quarter.Key, ["TASK-2"] = null })));
        Assert.Contains("missing: TASK-2", missing.Message);
        Assert.Contains("only under monthly goals", notMonthly.Message);

        await goals.DeleteAsync(old.Id,
            new DeleteGoalRequest(GoalTaskActions.Reassign, new Dictionary<string, string?> { ["TASK-1"] = october.Key, ["TASK-2"] = null }));

        Assert.Equal(october.Id, db.BoardTasks.Single(t => t.Id == first.Id).GoalId);
        Assert.Null(db.BoardTasks.Single(t => t.Id == second.Id).GoalId);
    }
}
