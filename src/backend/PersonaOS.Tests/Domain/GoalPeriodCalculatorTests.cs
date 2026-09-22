using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

/// <summary>
/// A goal runs between two dates the user picks, and its period type bounds them: a month at most
/// 31 days, a quarter at most 90, a year always to 31 December of its start year. Both ends count.
/// Owner's rules, 2026-09-22 — they replace snapping every start to the 1st, which left
/// "Complete the course by Oct 15th" filed as the whole of October with the date in the title.
/// </summary>
public class GoalPeriodCalculatorTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Theory]
    [InlineData("month", "2026-09-01", "2026-09-30")]
    [InlineData("month", "2026-09-22", "2026-10-21")]
    [InlineData("month", "2026-01-31", "2026-02-27")] // never longer than 31 days
    [InlineData("quarter", "2026-09-22", "2026-12-20")] // 90 days, both ends counted
    [InlineData("year", "2026-09-22", "2026-12-31")]
    [InlineData("year", "2027-03-01", "2027-12-31")] // a future start is fine
    public void Default_end_follows_the_period_type(string type, string start, string expected)
    {
        var end = GoalPeriodCalculator.DefaultEnd(type, D(start));

        Assert.Equal(D(expected), end);
        Assert.Null(GoalPeriodCalculator.Problem(type, D(start), end));
    }

    [Theory]
    [InlineData("month", "2026-10-01", "2026-10-31")] // 31 days: the longest month allowed
    [InlineData("month", "2026-09-22", "2026-10-15")] // "by Oct 15th"
    [InlineData("month", "2026-09-22", "2026-09-22")] // a single day
    [InlineData("quarter", "2026-09-22", "2026-12-20")] // exactly 90 days
    [InlineData("quarter", "2026-09-22", "2026-10-01")]
    [InlineData("year", "2026-02-10", "2026-12-31")]
    public void Accepts_periods_within_the_rules(string type, string start, string end)
    {
        Assert.Null(GoalPeriodCalculator.Problem(type, D(start), D(end)));
    }

    [Theory]
    [InlineData("month", "2026-10-01", "2026-11-01", "at most 31 days")] // 32 days
    [InlineData("quarter", "2026-09-22", "2026-12-21", "at most 90 days")] // 91 days
    [InlineData("year", "2026-02-10", "2026-12-30", "31 December")]
    [InlineData("year", "2026-02-10", "2027-01-31", "31 December")]
    [InlineData("month", "2026-10-15", "2026-10-01", "before the start")]
    public void Refuses_periods_that_break_them(string type, string start, string end, string reason)
    {
        var problem = GoalPeriodCalculator.Problem(type, D(start), D(end));

        Assert.NotNull(problem);
        Assert.Contains(reason, problem);
    }

    [Fact]
    public void Says_how_to_fix_an_over_long_month()
    {
        // The message goes to the model as a tool error, so it has to name a way out.
        var problem = GoalPeriodCalculator.Problem(GoalPeriods.Month, D("2026-10-01"), D("2026-11-15"));

        Assert.Contains("quarterly", problem);
        Assert.Contains("2026-10-31", problem);
    }

    [Fact]
    public void Counts_both_ends()
    {
        Assert.Equal(31, GoalPeriodCalculator.Days(D("2026-10-01"), D("2026-10-31")));
        Assert.Equal(1, GoalPeriodCalculator.Days(D("2026-10-01"), D("2026-10-01")));
    }
}
