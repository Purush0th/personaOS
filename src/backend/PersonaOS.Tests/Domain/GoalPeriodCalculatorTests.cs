using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

/// <summary>
/// A goal's period start must be the first day of its period. Nothing enforced that, so a
/// model filed a *monthly* goal starting 2026-10-10 — not a period at all, and it breaks
/// ordering and grouping. Normalising in the domain fixes it for every caller and every model.
/// </summary>
public class GoalPeriodCalculatorTests
{
    [Theory]
    [InlineData("2026-10-10", "2026-10-01")] // the observed case
    [InlineData("2026-09-11", "2026-09-01")]
    [InlineData("2026-01-31", "2026-01-01")]
    [InlineData("2026-12-01", "2026-12-01")] // already correct, unchanged
    public void Month_snaps_to_the_first_of_that_month(string input, string expected)
    {
        var result = GoalPeriodCalculator.NormalizeStart(GoalPeriods.Month, DateOnly.Parse(input));

        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Theory]
    [InlineData("2026-01-15", "2026-01-01")]
    [InlineData("2026-03-31", "2026-01-01")]
    [InlineData("2026-04-01", "2026-04-01")]
    [InlineData("2026-06-30", "2026-04-01")]
    [InlineData("2026-09-11", "2026-07-01")]
    [InlineData("2026-10-10", "2026-10-01")]
    [InlineData("2026-12-31", "2026-10-01")]
    public void Quarter_snaps_to_the_first_day_of_its_quarter(string input, string expected)
    {
        var result = GoalPeriodCalculator.NormalizeStart(GoalPeriods.Quarter, DateOnly.Parse(input));

        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Theory]
    [InlineData("2026-09-11", "2026-01-01")]
    [InlineData("2026-01-01", "2026-01-01")]
    [InlineData("2026-12-31", "2026-01-01")]
    public void Year_snaps_to_january_first(string input, string expected)
    {
        var result = GoalPeriodCalculator.NormalizeStart(GoalPeriods.Year, DateOnly.Parse(input));

        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Fact]
    public void An_unknown_period_type_is_left_alone_rather_than_throwing()
    {
        // Validation rejects bad types elsewhere; this must not be what blows up.
        var input = new DateOnly(2026, 10, 10);

        Assert.Equal(input, GoalPeriodCalculator.NormalizeStart("fortnight", input));
    }
}
