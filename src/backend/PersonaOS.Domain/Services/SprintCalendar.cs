namespace PersonaOS.Domain.Services;

/// <summary>
/// The fixed weekly rhythm of the sprint board, in the user's local time:
/// a sprint closes on Sunday at 18:00, the planning nudge goes out at 19:00, and the next sprint
/// starts at 20:00. Pure wall-clock arithmetic; converting to and from UTC is the caller's job.
/// </summary>
public static class SprintCalendar
{
    public static readonly TimeOnly CloseTime = new(18, 0);
    public static readonly TimeOnly NudgeTime = new(19, 0);
    public static readonly TimeOnly StartTime = new(20, 0);

    /// <summary>The first Sunday 18:00 strictly after <paramref name="local"/>.</summary>
    public static DateTime NextClose(DateTime local)
    {
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)local.DayOfWeek + 7) % 7;
        var candidate = local.Date.AddDays(daysUntilSunday).Add(CloseTime.ToTimeSpan());
        return candidate > local ? candidate : candidate.AddDays(7);
    }

    /// <summary>
    /// True between Sunday 18:00 and 20:00, when the last sprint has closed and the next is
    /// being planned.
    /// </summary>
    public static bool InPlanningWindow(DateTime local) =>
        local.DayOfWeek == DayOfWeek.Sunday
        && TimeOnly.FromDateTime(local) >= CloseTime
        && TimeOnly.FromDateTime(local) < StartTime;

    /// <summary>The Sunday 20:00 start that follows a close at <paramref name="closeLocal"/>.</summary>
    public static DateTime StartAfterClose(DateTime closeLocal) =>
        closeLocal.Date.Add(StartTime.ToTimeSpan());

    /// <summary>The close one week after a start at <paramref name="startLocal"/>.</summary>
    public static DateTime CloseAfterStart(DateTime startLocal) => NextClose(startLocal);

    /// <summary>The planning nudge for a sprint starting at <paramref name="startLocal"/>.</summary>
    public static DateTime NudgeBeforeStart(DateTime startLocal) =>
        startLocal.Date.Add(NudgeTime.ToTimeSpan());
}
