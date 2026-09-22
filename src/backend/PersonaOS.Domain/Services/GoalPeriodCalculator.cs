using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>
/// The dates a goal runs between, and the rules they must keep.
///
/// A goal starts on any day the user picks — future dates included — and its period type sets
/// how long it may run:
/// <list type="bullet">
/// <item>a month goal spans at most <see cref="MaxMonthDays"/> days,</item>
/// <item>a quarter goal at most <see cref="MaxQuarterDays"/>,</item>
/// <item>a year goal always ends on 31 December of the year it starts in.</item>
/// </list>
/// Both ends count: 1–31 Oct is 31 days.
///
/// This replaced snapping every start to the first day of its month, quarter or year. That fixed
/// a model filing a monthly goal on the 10th, but it also meant "Complete the course by Oct
/// 15th" was stored as the whole of October with the real date left in the title, and nothing
/// knew when a goal was due.
/// </summary>
public static class GoalPeriodCalculator
{
    public const int MaxMonthDays = 31;
    public const int MaxQuarterDays = 90;

    /// <summary>
    /// The end a goal gets when none is given: a month on from the start less a day, 90 days
    /// for a quarter, 31 December for a year. Unknown period types end where they start —
    /// validation rejects them elsewhere, and this should not be the thing that throws.
    /// </summary>
    public static DateOnly DefaultEnd(string periodType, DateOnly start) => periodType switch
    {
        GoalPeriods.Year => EndOfYear(start),
        GoalPeriods.Quarter => start.AddDays(MaxQuarterDays - 1),
        GoalPeriods.Month => start.AddMonths(1).AddDays(-1),
        _ => start,
    };

    /// <summary>Days from <paramref name="start"/> to <paramref name="end"/>, counting both.</summary>
    public static int Days(DateOnly start, DateOnly end) => end.DayNumber - start.DayNumber + 1;

    /// <summary>
    /// Why <paramref name="start"/>–<paramref name="end"/> is not a valid
    /// <paramref name="periodType"/> goal, in words for the user, or null when it is.
    /// </summary>
    public static string? Problem(string periodType, DateOnly start, DateOnly end)
    {
        if (end < start)
            return $"The end date {end:yyyy-MM-dd} is before the start date {start:yyyy-MM-dd}.";

        var days = Days(start, end);
        return periodType switch
        {
            GoalPeriods.Month when days > MaxMonthDays =>
                $"A monthly goal runs at most {MaxMonthDays} days; {start:yyyy-MM-dd} to {end:yyyy-MM-dd} "
                + $"is {days}. Make it a quarterly goal, or end it by {start.AddDays(MaxMonthDays - 1):yyyy-MM-dd}.",
            GoalPeriods.Quarter when days > MaxQuarterDays =>
                $"A quarterly goal runs at most {MaxQuarterDays} days; {start:yyyy-MM-dd} to {end:yyyy-MM-dd} "
                + $"is {days}. End it by {start.AddDays(MaxQuarterDays - 1):yyyy-MM-dd}, or make it yearly.",
            GoalPeriods.Year when end != EndOfYear(start) =>
                $"A yearly goal ends on 31 December of the year it starts, so one starting "
                + $"{start:yyyy-MM-dd} ends {EndOfYear(start):yyyy-MM-dd}.",
            _ => null,
        };
    }

    private static DateOnly EndOfYear(DateOnly date) => new(date.Year, 12, 31);
}
