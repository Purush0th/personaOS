using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class GoalCalendarTests
{
    private static readonly DateOnly Sep27 = new(2026, 9, 27);

    private static (GoalDates? Dates, string? Problem) Resolve(string type, int? year, int? slot, DateOnly? start = null) =>
        GoalCalendar.Resolve(type, year, slot, start, Sep27);

    [Fact]
    public void A_year_runs_from_today_or_a_later_start_to_31_December()
    {
        Assert.Equal(new GoalDates(Sep27, new DateOnly(2026, 12, 31)), Resolve(GoalPeriods.Year, null, null).Dates);
        Assert.Equal(new DateOnly(2027, 1, 1), Resolve(GoalPeriods.Year, 2027, null).Dates!.Value.Start);
        Assert.Contains("in the past", Resolve(GoalPeriods.Year, null, null, new DateOnly(2026, 9, 1)).Problem);
    }

    [Fact]
    public void A_year_needs_90_days()
    {
        Assert.NotNull(Resolve(GoalPeriods.Year, null, null, new DateOnly(2026, 10, 3)).Dates);   // 90 days
        Assert.Contains("at least 90", Resolve(GoalPeriods.Year, null, null, new DateOnly(2026, 10, 4)).Problem);
    }

    [Theory]
    [InlineData(3, "at least 45")]      // 4 days left
    [InlineData(4, null)]
    public void A_quarter_needs_45_days_left(int quarter, string? problem)
    {
        var (dates, text) = Resolve(GoalPeriods.Quarter, 2026, quarter);
        if (problem is null) Assert.Equal(new GoalDates(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)), dates);
        else Assert.Contains(problem, text);
    }

    [Fact]
    public void A_month_needs_15_days_left_and_starts_today_when_under_way()
    {
        var today = new DateOnly(2026, 10, 17);
        Assert.Equal(new GoalDates(today, new DateOnly(2026, 10, 31)),
            GoalCalendar.Resolve(GoalPeriods.Month, 2026, 10, null, today).Dates);            // 15 days
        Assert.Contains("at least 15",
            GoalCalendar.Resolve(GoalPeriods.Month, 2026, 10, null, today.AddDays(1)).Problem);
    }

    [Fact]
    public void A_day_without_a_slot_picks_the_slot_it_falls_in()
    {
        Assert.Equal("Q4 2026", GoalCalendar.Label(GoalPeriods.Quarter,
            Resolve(GoalPeriods.Quarter, null, null, new DateOnly(2026, 11, 5)).Dates!.Value.End));
    }

    [Fact]
    public void A_child_sits_in_its_parent_and_starts_no_earlier()
    {
        var parent = (GoalPeriods.Year, new GoalDates(new DateOnly(2026, 10, 10), new DateOnly(2026, 12, 31)));

        var q4 = GoalCalendar.Resolve(GoalPeriods.Quarter, 2026, 4, null, Sep27, parent);
        var q1 = GoalCalendar.Resolve(GoalPeriods.Quarter, 2027, 1, null, Sep27, parent);

        Assert.Equal(new DateOnly(2026, 10, 10), q4.Dates!.Value.Start);
        Assert.Contains("not in 2026", q1.Problem);
    }

    [Theory]
    [InlineData(GoalPeriods.Year, "2026")]
    [InlineData(GoalPeriods.Quarter, "Q4 2026")]
    [InlineData(GoalPeriods.Month, "Dec 2026")]
    public void Slots_read_as_the_user_says_them(string type, string label)
    {
        Assert.Equal(label, GoalCalendar.Label(type, new DateOnly(2026, 12, 31)));
    }
}
