using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

/// <summary>2026-09-13 and 2026-09-20 are Sundays.</summary>
public class SprintCalendarTests
{
    [Theory]
    [InlineData("2026-09-15 09:00", "2026-09-20 18:00")] // Tuesday
    [InlineData("2026-09-19 23:59", "2026-09-20 18:00")] // Saturday night
    [InlineData("2026-09-20 17:59", "2026-09-20 18:00")] // a minute before close
    [InlineData("2026-09-20 18:00", "2026-09-27 18:00")] // at close: the next one
    [InlineData("2026-09-20 21:00", "2026-09-27 18:00")] // Sunday after the start
    public void Next_close_is_the_coming_Sunday_at_18(string now, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), SprintCalendar.NextClose(DateTime.Parse(now)));
    }

    [Theory]
    [InlineData("2026-09-20 17:59", false)]
    [InlineData("2026-09-20 18:00", true)]
    [InlineData("2026-09-20 19:30", true)]
    [InlineData("2026-09-20 20:00", false)]
    [InlineData("2026-09-19 19:00", false)] // Saturday
    public void The_planning_window_is_Sunday_18_to_20(string now, bool expected)
    {
        Assert.Equal(expected, SprintCalendar.InPlanningWindow(DateTime.Parse(now)));
    }

    [Fact]
    public void A_sprint_starts_at_20_and_closes_the_next_Sunday_at_18()
    {
        var start = SprintCalendar.StartAfterClose(DateTime.Parse("2026-09-20 18:00"));

        Assert.Equal(DateTime.Parse("2026-09-20 20:00"), start);
        Assert.Equal(DateTime.Parse("2026-09-27 18:00"), SprintCalendar.CloseAfterStart(start));
        Assert.Equal(DateTime.Parse("2026-09-20 19:00"), SprintCalendar.NudgeBeforeStart(start));
    }
}
