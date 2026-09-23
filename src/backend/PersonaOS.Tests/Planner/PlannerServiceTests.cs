using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Planner;

public class PlannerServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 23);

    private static (TestDbContext Db, PlannerService Planner) Setup()
    {
        var db = TestDbContext.Create();
        return (db, new PlannerService(db));
    }

    [Fact]
    public async Task A_day_lists_timed_items_first_then_the_rest_in_order()
    {
        var (_, planner) = Setup();
        await planner.CreateAsync(new CreatePlannerItemRequest("Read", Day));
        await planner.CreateAsync(new CreatePlannerItemRequest("Gym", Day, ScheduledTime: new TimeOnly(7, 30)));
        await planner.CreateAsync(new CreatePlannerItemRequest("Write", Day));
        await planner.CreateAsync(new CreatePlannerItemRequest("Standup", Day, ScheduledTime: new TimeOnly(9, 0)));

        var day = await planner.GetDayAsync(Day);

        Assert.Equal(["Gym", "Standup", "Read", "Write"], day.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task A_range_groups_by_day_in_order_and_skips_empty_days()
    {
        var (_, planner) = Setup();
        await planner.CreateAsync(new CreatePlannerItemRequest("Later", Day.AddDays(2)));
        await planner.CreateAsync(new CreatePlannerItemRequest("First", Day));

        var days = await planner.GetRangeAsync(Day, Day.AddDays(6));

        Assert.Equal([Day, Day.AddDays(2)], days.Select(d => d.Date));
    }

    [Fact]
    public async Task A_range_must_run_forwards_and_stay_within_a_year()
    {
        var (_, planner) = Setup();

        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.GetRangeAsync(Day, Day.AddDays(-1)));
        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.GetRangeAsync(Day, Day.AddDays(400)));
    }

    [Fact]
    public async Task An_item_picked_from_the_board_takes_the_tasks_title_and_goal()
    {
        var (db, planner) = Setup();
        var goal = new Goal { Number = 1, Title = "Ship", PeriodStart = Day, PeriodEnd = Day };
        var task = new BoardTask { Number = 4, Title = "File taxes", Goal = goal };
        db.BoardTasks.Add(task);
        await db.SaveChangesAsync();

        var item = await planner.CreateAsync(new CreatePlannerItemRequest("", Day, TaskId: task.Id));

        Assert.Equal("File taxes", item.Title);
        Assert.Equal("TASK-4", item.TaskKey);
        Assert.Equal("GOAL-1", item.GoalKey);
    }

    [Fact]
    public async Task Refuses_an_item_with_no_title_or_a_goal_or_task_that_is_not_there()
    {
        var (_, planner) = Setup();

        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(new CreatePlannerItemRequest("  ", Day)));
        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(new CreatePlannerItemRequest("x", Day, GoalId: 99)));
        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.CreateAsync(new CreatePlannerItemRequest("x", Day, TaskId: 99)));
    }

    [Fact]
    public async Task Status_changes_accept_only_known_statuses()
    {
        var (_, planner) = Setup();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Gym", Day));

        Assert.Equal(PlannerItemStatuses.Done, (await planner.UpdateStatusAsync(item.Id, " DONE "))!.Status);
        await Assert.ThrowsAsync<PlannerValidationException>(() => planner.UpdateStatusAsync(item.Id, "finished"));
        Assert.Null(await planner.UpdateStatusAsync(999, "done"));
    }

    [Fact]
    public async Task Moving_an_item_puts_it_at_the_end_of_its_new_day()
    {
        var (_, planner) = Setup();
        var tomorrow = Day.AddDays(1);
        await planner.CreateAsync(new CreatePlannerItemRequest("Already there", tomorrow));
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Unfinished", Day));

        await planner.MoveAsync(item.Id, tomorrow);

        Assert.Equal(["Already there", "Unfinished"], (await planner.GetDayAsync(tomorrow)).Items.Select(i => i.Title));
        Assert.Empty((await planner.GetDayAsync(Day)).Items);
    }

    [Fact]
    public async Task An_update_can_clear_the_time_and_the_goal()
    {
        var (db, planner) = Setup();
        var goal = new Goal { Number = 1, Title = "Ship", PeriodStart = Day, PeriodEnd = Day };
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
        var item = await planner.CreateAsync(new CreatePlannerItemRequest("Gym", Day, ScheduledTime: new TimeOnly(7, 0), GoalId: goal.Id));

        var updated = await planner.UpdateAsync(item.Id, new UpdatePlannerItemRequest(ClearScheduledTime: true, ClearGoal: true));

        Assert.Null(updated!.ScheduledTime);
        Assert.Null(updated.GoalId);
    }

    [Fact]
    public async Task Deleting_something_that_is_not_there_says_so()
    {
        var (_, planner) = Setup();

        Assert.False(await planner.DeleteAsync(42));
    }
}
