using PersonaOS.Application.Common;

namespace PersonaOS.Tests.Common;

/// <summary>
/// Time-zone handling underpins reminders ("tomorrow at 9am") and the planner's
/// notion of "today", so the conversions and their fallbacks are pinned here.
/// </summary>
public class UserClockTests
{
    // Windows and IANA ids both resolve on .NET; use IANA as the app does.
    private const string India = "Asia/Kolkata";
    private const string NewYork = "America/New_York";

    [Fact]
    public void Local_time_converts_to_the_right_utc_instant()
    {
        // IST is UTC+5:30 year-round.
        var utc = UserClock.ToUtc(new DateTime(2026, 8, 1, 9, 0, 0), India);

        Assert.Equal(new DateTime(2026, 8, 1, 3, 30, 0), utc);
    }

    [Fact]
    public void Utc_renders_back_to_the_original_local_time()
    {
        var local = new DateTime(2026, 8, 1, 9, 0, 0);

        var roundTripped = UserClock.ToLocal(UserClock.ToUtc(local, India), India);

        Assert.Equal(local, roundTripped);
    }

    [Fact]
    public void Daylight_saving_is_honoured_for_zones_that_observe_it()
    {
        // EDT (UTC-4) in July, EST (UTC-5) in January.
        var summer = UserClock.ToUtc(new DateTime(2026, 7, 1, 12, 0, 0), NewYork);
        var winter = UserClock.ToUtc(new DateTime(2026, 1, 1, 12, 0, 0), NewYork);

        Assert.Equal(16, summer.Hour);
        Assert.Equal(17, winter.Hour);
    }

    [Fact]
    public void A_time_inside_the_spring_forward_gap_is_shifted_forward()
    {
        // 2026-03-08 02:30 does not exist in New York; it must not throw.
        var utc = UserClock.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), NewYork);

        // Nudged to 03:30 EDT = 07:30 UTC.
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0), utc);
    }

    [Fact]
    public void Unknown_zone_falls_back_to_utc_instead_of_throwing()
    {
        var local = new DateTime(2026, 8, 1, 9, 0, 0);

        Assert.Equal(local, UserClock.ToUtc(local, "Not/AZone"));
        Assert.Equal(local, UserClock.ToLocal(local, "Not/AZone"));
    }

    [Fact]
    public void Today_reflects_the_users_zone_not_the_servers()
    {
        // Somewhere on Earth it is always a valid date; assert the conversion
        // matches an independent computation rather than a hard-coded day.
        var expected = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(India)));

        Assert.Equal(expected, UserClock.Today(India));
    }

    [Fact]
    public void Zones_ahead_and_behind_utc_can_report_different_dates()
    {
        var kiritimati = UserClock.Today("Pacific/Kiritimati"); // UTC+14
        var midway = UserClock.Today("Pacific/Midway");         // UTC-11

        // They are never more than one day apart, and never inverted.
        Assert.InRange(kiritimati.DayNumber - midway.DayNumber, 0, 1);
    }
}
