namespace PersonaOS.Application.Common;

/// <summary>
/// Converts between UTC (how everything is stored) and the install's configured
/// time zone (how the user thinks). Unknown zone ids fall back to UTC rather than
/// throwing, so a bad config value degrades instead of breaking the app.
/// </summary>
public static class UserClock
{
    public static TimeZoneInfo Zone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The user's current local date.</summary>
    public static DateOnly Today(string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone(timeZoneId)));

    /// <summary>Renders a stored UTC instant in the user's zone.</summary>
    public static DateTime ToLocal(DateTime utc, string timeZoneId) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(timeZoneId));

    /// <summary>
    /// Interprets a wall-clock time the user meant in their zone and returns the UTC instant.
    /// Handles DST gaps (spring-forward) by shifting forward out of the invalid hour.
    /// </summary>
    public static DateTime ToUtc(DateTime local, string timeZoneId)
    {
        var zone = Zone(timeZoneId);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // A time inside the spring-forward gap doesn't exist locally; nudge past it.
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }
}
