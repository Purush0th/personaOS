using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>The days a goal runs, both ends included.</summary>
public readonly record struct GoalDates(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

/// <summary>
/// Where a goal sits on the calendar, and the rules its dates keep (owner's rules, 2026-09-27).
///
/// Every goal follows the calendar. The user picks a type and a slot and the dates follow:
/// <list type="bullet">
/// <item>a <b>year</b> starts today or on a later day the user picks and ends on 31 December of
/// that year, and needs at least <see cref="MinYearDays"/> days;</item>
/// <item>a <b>quarter</b> is Q1–Q4 of a year; it ends on the quarter's last day and needs at
/// least <see cref="MinQuarterDays"/> days;</item>
/// <item>a <b>month</b> is one calendar month; it ends on the month's last day and needs at
/// least <see cref="MinMonthDays"/> days.</item>
/// </list>
/// A goal never starts in the past: a slot already under way starts today, and a slot with too
/// few days left is refused (a Q3 goal on 27 September would have four days). A nested goal also
/// starts no earlier than its parent, and its slot must lie in the parent's year or quarter.
/// </summary>
public static class GoalCalendar
{
    public const int MinYearDays = 90;
    public const int MinQuarterDays = 45;
    public const int MinMonthDays = 15;

    /// <summary>The type a goal's parent must have, or null when the type cannot be nested.</summary>
    public static string? ParentTypeOf(string type) => type switch
    {
        GoalPeriods.Quarter => GoalPeriods.Year,
        GoalPeriods.Month => GoalPeriods.Quarter,
        _ => null,
    };

    /// <summary>The most child goals a goal of this type holds: one per slot.</summary>
    public static int MaxChildren(string type) => type switch
    {
        GoalPeriods.Year => 4,
        GoalPeriods.Quarter => 3,
        _ => 0,
    };

    public static int QuarterOf(DateOnly date) => (date.Month - 1) / 3 + 1;

    public static DateOnly QuarterStart(int year, int quarter) => new(year, (quarter - 1) * 3 + 1, 1);

    public static DateOnly QuarterEnd(int year, int quarter) => QuarterStart(year, quarter).AddMonths(3).AddDays(-1);

    public static DateOnly MonthEnd(int year, int month) => new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);

    public static DateOnly YearEnd(int year) => new(year, 12, 31);

    /// <summary>
    /// The slot number a goal holds: its quarter (1–4) for a quarter, its month (1–12) for a
    /// month, null for a year. Read from the end date, which is always the slot's last day.
    /// </summary>
    public static int? SlotOf(string type, DateOnly end) => type switch
    {
        GoalPeriods.Quarter => QuarterOf(end),
        GoalPeriods.Month => end.Month,
        _ => null,
    };

    /// <summary>"2026", "Q4 2026" or "Oct 2026" — how a goal's slot reads to the user.</summary>
    public static string Label(string type, DateOnly end) => type switch
    {
        GoalPeriods.Quarter => $"Q{QuarterOf(end)} {end.Year}",
        GoalPeriods.Month => $"{end:MMM yyyy}",
        _ => $"{end.Year}",
    };

    /// <summary>
    /// The dates for a goal of <paramref name="type"/>, or why there are none, in words for the
    /// user.
    /// </summary>
    /// <param name="year">The slot's year. For a year goal, a future year starts on 1 January.</param>
    /// <param name="slot">The quarter (1–4) or month (1–12). Unused for a year.</param>
    /// <param name="start">
    /// A year goal's chosen start. For a quarter or month given no slot, the day it names picks
    /// the slot (so "2026-10-01" as a quarter is Q4 2026).
    /// </param>
    /// <param name="today">The user's local today.</param>
    /// <param name="parent">The parent's type and dates when the goal is nested.</param>
    public static (GoalDates? Dates, string? Problem) Resolve(
        string type, int? year, int? slot, DateOnly? start, DateOnly today,
        (string Type, GoalDates Dates)? parent = null)
    {
        return type switch
        {
            GoalPeriods.Year => ResolveYear(year, start, today),
            GoalPeriods.Quarter => ResolveSlot(type, year ?? start?.Year, slot ?? (start is { } q ? QuarterOf(q) : null), today, parent),
            GoalPeriods.Month => ResolveSlot(type, year ?? start?.Year, slot ?? start?.Month, today, parent),
            _ => (null, $"Period type must be one of: {string.Join(", ", GoalPeriods.All)}."),
        };
    }

    private static (GoalDates?, string?) ResolveYear(int? year, DateOnly? start, DateOnly today)
    {
        var first = start ?? (year is int y && y > today.Year ? new DateOnly(y, 1, 1) : today);
        if (year is int wanted && first.Year != wanted)
            return (null, $"A yearly goal for {wanted} must start in {wanted}; {first:yyyy-MM-dd} is not.");
        if (first < today)
            return (null, $"A goal cannot start in the past; start it on {today:yyyy-MM-dd} or later.");

        var dates = new GoalDates(first, YearEnd(first.Year));
        if (dates.Days < MinYearDays)
        {
            var latest = YearEnd(first.Year).AddDays(-(MinYearDays - 1));
            return (null,
                $"A yearly goal needs at least {MinYearDays} days, and one starting {first:yyyy-MM-dd} has {dates.Days}. "
                + $"Start it by {latest:yyyy-MM-dd}, make it a quarterly goal, or make it a goal for {first.Year + 1}.");
        }
        return (dates, null);
    }

    private static (GoalDates?, string?) ResolveSlot(
        string type, int? year, int? slot, DateOnly today, (string Type, GoalDates Dates)? parent)
    {
        var isQuarter = type == GoalPeriods.Quarter;
        var what = isQuarter ? "quarter (Q1–Q4)" : "month";
        if (slot is null) return (null, $"Pick a {what} and a year.");

        // "November" with no year means the parent's November, or the next one: this year's while
        // it has not ended, next year's after.
        if (year is null && slot is >= 1 and <= 12 && (!isQuarter || slot <= 4))
        {
            if (parent is { } within) year = within.Dates.End.Year;
            else
            {
                var endThisYear = isQuarter ? QuarterEnd(today.Year, slot.Value) : MonthEnd(today.Year, slot.Value);
                year = endThisYear < today ? today.Year + 1 : today.Year;
            }
        }
        if (year is null) return (null, $"Pick a {what} and a year.");
        if (isQuarter ? slot is < 1 or > 4 : slot is < 1 or > 12)
            return (null, isQuarter ? "A quarter is Q1, Q2, Q3 or Q4." : "A month is 1 to 12.");
        if (year < today.Year) return (null, $"{year} has already passed.");

        var slotStart = isQuarter ? QuarterStart(year.Value, slot.Value) : new DateOnly(year.Value, slot.Value, 1);
        var slotEnd = isQuarter ? QuarterEnd(year.Value, slot.Value) : MonthEnd(year.Value, slot.Value);
        var label = Label(type, slotEnd);
        if (slotEnd < today) return (null, $"{label} has already ended.");

        var first = slotStart < today ? today : slotStart;
        if (parent is { } p)
        {
            // The slot must be one of the parent's own: a quarter of its year, a month of its quarter.
            var (parentStart, parentEnd) = p.Type == GoalPeriods.Year
                ? (new DateOnly(p.Dates.End.Year, 1, 1), p.Dates.End)
                : (QuarterStart(p.Dates.End.Year, QuarterOf(p.Dates.End)), p.Dates.End);
            if (slotStart < parentStart || slotEnd > parentEnd)
                return (null, $"{label} is not in {Label(p.Type, p.Dates.End)}.");
            if (p.Dates.Start > first) first = p.Dates.Start;
        }

        var dates = new GoalDates(first, slotEnd);
        var min = isQuarter ? MinQuarterDays : MinMonthDays;
        if (dates.Days < min)
        {
            return (null,
                $"{label} has only {dates.Days} days left from {first:yyyy-MM-dd}; a {(isQuarter ? "quarterly" : "monthly")} "
                + $"goal needs at least {min}. Pick a later {(isQuarter ? "quarter" : "month")}.");
        }
        return (dates, null);
    }
}
