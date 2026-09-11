using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>
/// Snaps a goal's <c>PeriodStart</c> to the first day of the period it belongs to.
///
/// The tool contract says "periodStart is the first day of the period", but nothing used to
/// enforce it, so a model could file a *monthly* goal starting on the 10th — observed in the
/// wild. That is not a meaningful period, and it breaks grouping and ordering by period.
/// Normalising here makes the stored data correct whichever model is driving, rather than
/// relying on the model to get the arithmetic right.
/// </summary>
public static class GoalPeriodCalculator
{
    /// <summary>
    /// The first day of the <paramref name="periodType"/> period containing
    /// <paramref name="start"/>. Unknown period types are returned unchanged — validation
    /// elsewhere rejects them, and this should not be the thing that throws.
    /// </summary>
    public static DateOnly NormalizeStart(string periodType, DateOnly start) => periodType switch
    {
        GoalPeriods.Year => new DateOnly(start.Year, 1, 1),
        GoalPeriods.Quarter => new DateOnly(start.Year, FirstMonthOfQuarter(start.Month), 1),
        GoalPeriods.Month => new DateOnly(start.Year, start.Month, 1),
        _ => start,
    };

    private static int FirstMonthOfQuarter(int month) => month switch
    {
        <= 3 => 1,
        <= 6 => 4,
        <= 9 => 7,
        _ => 10,
    };
}
