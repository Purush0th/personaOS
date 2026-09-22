using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Goals;

/// <summary>
/// A goal has a start and an end. The service is where the rules hold for every caller — the web
/// form, the phone, the API and the assistant — so it is tested here, not only in the calculator.
/// </summary>
public class GoalDatesTests
{
    private static GoalService Service() => new(TestDbContext.Create());

    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Fact]
    public async Task A_goal_keeps_the_start_it_was_given_and_gets_a_default_end()
    {
        // The old behaviour snapped this to 2026-09-01; the owner picks the start now.
        var goal = await Service().CreateAsync(
            new CreateGoalRequest("Finish the course", null, GoalPeriods.Month, D("2026-09-22")));

        Assert.Equal(D("2026-09-22"), goal.PeriodStart);
        Assert.Equal(D("2026-10-21"), goal.PeriodEnd);
    }

    [Fact]
    public async Task A_deadline_is_stored_as_the_end()
    {
        // "Complete LLM Engineering Course by Oct 15th" — the date belongs in a field.
        var goal = await Service().CreateAsync(new CreateGoalRequest(
            "Complete LLM Engineering Course", null, GoalPeriods.Month, D("2026-09-22"),
            PeriodEnd: D("2026-10-15")));

        Assert.Equal(D("2026-10-15"), goal.PeriodEnd);
    }

    [Fact]
    public async Task A_future_goal_is_allowed()
    {
        var goal = await Service().CreateAsync(
            new CreateGoalRequest("Run a half marathon", null, GoalPeriods.Quarter, D("2027-01-10")));

        Assert.Equal(D("2027-04-09"), goal.PeriodEnd);
    }

    [Theory]
    [InlineData("month", "2026-10-01", "2026-11-05")]
    [InlineData("quarter", "2026-10-01", "2027-01-15")]
    [InlineData("year", "2026-10-01", "2027-06-30")]
    [InlineData("month", "2026-10-15", "2026-10-01")]
    public async Task Dates_that_break_the_rules_are_refused(string type, string start, string end)
    {
        await Assert.ThrowsAsync<GoalValidationException>(() => Service().CreateAsync(
            new CreateGoalRequest("Too long", null, type, D(start), PeriodEnd: D(end))));
    }

    [Fact]
    public async Task A_goal_without_a_start_is_refused()
    {
        // POST /api/goals used to accept a missing periodStart and store 0001-01-01.
        var error = await Assert.ThrowsAsync<GoalValidationException>(() => Service().CreateAsync(
            new CreateGoalRequest("No start", null, GoalPeriods.Month, default)));

        Assert.Contains("start date", error.Message);
    }

    [Fact]
    public async Task Changing_the_type_gives_that_type_its_default_end()
    {
        // Keeping the old end would usually break the new type's rules.
        var service = Service();
        var goal = await service.CreateAsync(
            new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, D("2026-09-22")));

        var updated = await service.UpdateAsync(goal.Id, new UpdateGoalRequest(PeriodType: GoalPeriods.Year));

        Assert.Equal(GoalPeriods.Year, updated!.PeriodType);
        Assert.Equal(D("2026-12-31"), updated.PeriodEnd);
    }

    [Fact]
    public async Task Moving_the_end_is_checked_against_the_rules()
    {
        var service = Service();
        var goal = await service.CreateAsync(
            new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, D("2026-09-22")));

        var moved = await service.UpdateAsync(goal.Id, new UpdateGoalRequest(PeriodEnd: D("2026-10-05")));
        Assert.Equal(D("2026-10-05"), moved!.PeriodEnd);

        await Assert.ThrowsAsync<GoalValidationException>(() =>
            service.UpdateAsync(goal.Id, new UpdateGoalRequest(PeriodEnd: D("2026-11-30"))));
    }
}
