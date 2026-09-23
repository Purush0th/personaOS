using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Goals;

public class GoalServiceTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);

    private static (TestDbContext Db, GoalService Goals) Setup()
    {
        var db = TestDbContext.Create();
        return (db, new GoalService(db));
    }

    private static CreateGoalRequest Month(string title) => new(title, null, GoalPeriods.Month, Start);

    [Fact]
    public async Task Goals_get_sequential_keys()
    {
        var (_, goals) = Setup();

        var first = await goals.CreateAsync(Month("Run"));
        var second = await goals.CreateAsync(Month("Read"));

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

    [Fact]
    public async Task Completing_a_goal_sets_its_progress_to_100()
    {
        var (_, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));

        var completed = await goals.UpdateStatusAsync(goal.Id, GoalStatuses.Completed);

        Assert.Equal(100, completed!.Progress);
    }

    [Fact]
    public async Task Refuses_an_unknown_status_an_empty_title_and_progress_out_of_range()
    {
        var (_, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));

        await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateStatusAsync(goal.Id, "paused"));
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.CreateAsync(Month("  ")));
        await Assert.ThrowsAsync<GoalValidationException>(() => goals.UpdateAsync(goal.Id, new UpdateGoalRequest(Progress: 101)));
    }

    [Fact]
    public async Task Dropped_goals_are_hidden_unless_asked_for()
    {
        var (_, goals) = Setup();
        await goals.CreateAsync(Month("Run"));
        var dropped = await goals.CreateAsync(Month("Knit"));
        await goals.UpdateStatusAsync(dropped.Id, GoalStatuses.Dropped);

        Assert.Equal(["Run"], (await goals.GetAllAsync()).Select(g => g.Title));
        Assert.Equal(2, (await goals.GetAllAsync(includeDropped: true)).Count);
    }

    [Fact]
    public async Task Deleting_a_goal_keeps_its_tasks_and_planner_items_as_standalone()
    {
        var (db, goals) = Setup();
        var goal = await goals.CreateAsync(Month("Run"));
        db.BoardTasks.Add(new BoardTask { Number = 1, Title = "Buy shoes", GoalId = goal.Id });
        db.PlannerItems.Add(new PlannerItem { Title = "5k", Date = Start, GoalId = goal.Id });
        await db.SaveChangesAsync();

        Assert.True(await goals.DeleteAsync(goal.Id));

        Assert.Null(db.BoardTasks.Single().GoalId);
        Assert.Null(db.PlannerItems.Single().GoalId);
        Assert.Empty(db.Goals);
    }
}
