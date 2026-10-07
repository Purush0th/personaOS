using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class RecurrenceRuleTests
{
    // 3 October 2026 is a Saturday.
    private static readonly DateOnly Sat3Oct = new(2026, 10, 3);

    private static RecurrenceRule Rule(string text)
    {
        Assert.True(RecurrenceRule.TryParse(text, out var rule, out var error), error);
        return rule;
    }

    private static List<DateOnly> Dates(string rule, DateOnly start, DateOnly from, DateOnly to) =>
        Rule(rule).Dates(start, from, to).ToList();

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    // --- Daily, weekly -----------------------------------------------------------------

    [Fact]
    public void Daily_gives_every_day_in_the_window_only()
    {
        var dates = Dates("FREQ=DAILY", Sat3Oct, D(10, 5), D(10, 8));

        Assert.Equal([D(10, 5), D(10, 6), D(10, 7), D(10, 8)], dates);
    }

    [Fact]
    public void A_window_before_the_first_occurrence_is_empty()
    {
        Assert.Empty(Dates("FREQ=DAILY", Sat3Oct, D(9, 1), D(10, 2)));
    }

    [Fact]
    public void An_interval_skips_days()
    {
        var dates = Dates("FREQ=DAILY;INTERVAL=3", Sat3Oct, D(10, 3), D(10, 13));

        Assert.Equal([D(10, 3), D(10, 6), D(10, 9), D(10, 12)], dates);
    }

    [Fact]
    public void Weekly_without_days_repeats_on_the_weekday_of_the_start()
    {
        var dates = Dates("FREQ=WEEKLY", Sat3Oct, D(10, 3), D(10, 31));

        Assert.Equal([D(10, 3), D(10, 10), D(10, 17), D(10, 24), D(10, 31)], dates);
    }

    [Fact]
    public void Weekly_on_several_days_gives_them_in_order_and_never_before_the_start()
    {
        // Starting on a Wednesday: Monday of that week is before the start and must not appear.
        var dates = Dates("FREQ=WEEKLY;BYDAY=MO,WE,FR", D(10, 7), D(10, 5), D(10, 16));

        Assert.Equal([D(10, 7), D(10, 9), D(10, 12), D(10, 14), D(10, 16)], dates);
    }

    [Fact]
    public void Every_other_week_keeps_the_weeks_of_the_series()
    {
        var dates = Dates("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", D(10, 5), D(10, 5), D(11, 10));

        Assert.Equal([D(10, 5), D(10, 19), D(11, 2)], dates);
    }

    [Fact]
    public void Every_weekday_skips_the_weekend()
    {
        var dates = Dates("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", D(10, 2), D(10, 2), D(10, 7));

        Assert.Equal([D(10, 2), D(10, 5), D(10, 6), D(10, 7)], dates);
    }

    // --- Monthly, yearly ---------------------------------------------------------------

    [Fact]
    public void Monthly_on_the_31st_skips_months_without_one()
    {
        var dates = Dates("FREQ=MONTHLY", D(1, 31), D(1, 1), D(6, 30));

        Assert.Equal([D(1, 31), D(3, 31), D(5, 31)], dates);
    }

    [Fact]
    public void Monthly_on_the_last_day_follows_the_month_length()
    {
        var dates = Dates("FREQ=MONTHLY;BYMONTHDAY=-1", D(1, 31), D(1, 1), D(4, 30));

        Assert.Equal([D(1, 31), D(2, 28), D(3, 31), D(4, 30)], dates);
    }

    [Fact]
    public void Monthly_on_the_second_Saturday()
    {
        var dates = Dates("FREQ=MONTHLY;BYDAY=2SA", D(10, 10), D(10, 1), D(12, 31));

        Assert.Equal([D(10, 10), D(11, 14), D(12, 12)], dates);
    }

    [Fact]
    public void Monthly_on_the_last_Friday()
    {
        var dates = Dates("FREQ=MONTHLY;BYDAY=-1FR", D(10, 30), D(10, 1), D(12, 31));

        Assert.Equal([D(10, 30), D(11, 27), D(12, 25)], dates);
    }

    [Fact]
    public void Every_two_months_steps_by_two()
    {
        var dates = Dates("FREQ=MONTHLY;INTERVAL=2", D(10, 3), D(10, 1), D(2, 28, 2027));

        Assert.Equal([D(10, 3), D(12, 3), D(2, 3, 2027)], dates);
    }

    [Fact]
    public void Yearly_on_the_29th_of_February_only_happens_in_leap_years()
    {
        var dates = Dates("FREQ=YEARLY", D(2, 29, 2028), D(1, 1, 2028), D(12, 31, 2033));

        Assert.Equal([D(2, 29, 2028), D(2, 29, 2032)], dates);
    }

    // --- Ends --------------------------------------------------------------------------

    [Fact]
    public void A_count_ends_the_series_even_far_from_the_window()
    {
        var rule = "FREQ=DAILY;COUNT=3";

        Assert.Equal([Sat3Oct, D(10, 4), D(10, 5)], Dates(rule, Sat3Oct, D(10, 1), D(12, 31)));
        Assert.Empty(Dates(rule, Sat3Oct, D(10, 6), D(12, 31)));
    }

    [Fact]
    public void A_count_is_counted_from_the_first_occurrence_not_the_window()
    {
        var dates = Dates("FREQ=WEEKLY;COUNT=4", Sat3Oct, D(10, 17), D(12, 31));

        Assert.Equal([D(10, 17), D(10, 24)], dates);
    }

    [Fact]
    public void Until_is_inclusive()
    {
        var dates = Dates("FREQ=WEEKLY;UNTIL=20261017", Sat3Oct, D(10, 1), D(12, 31));

        Assert.Equal([Sat3Oct, D(10, 10), D(10, 17)], dates);
    }

    [Fact]
    public void A_window_years_away_is_reached_without_walking_from_the_start()
    {
        var dates = Dates("FREQ=DAILY;INTERVAL=3", D(1, 1), D(6, 1, 2040), D(6, 10, 2040));

        Assert.NotEmpty(dates);
        Assert.All(dates, d => Assert.Equal(0, (d.DayNumber - D(1, 1).DayNumber) % 3));
        Assert.All(dates, d => Assert.InRange(d, D(6, 1, 2040), D(6, 10, 2040)));
    }

    [Fact]
    public void An_unbounded_search_for_a_rule_that_never_ends_stops_at_the_end_of_time()
    {
        // Must neither hang nor throw an overflow: the window is simply cut off at year 9999.
        var dates = Rule("FREQ=YEARLY").Dates(D(1, 1), D(1, 1, 9998), DateOnly.MaxValue).ToList();

        Assert.Equal([D(1, 1, 9998), D(1, 1, 9999)], dates);
    }

    // --- Splitting a series ------------------------------------------------------------

    [Fact]
    public void Ending_before_a_date_with_a_count_keeps_only_what_came_before()
    {
        var rule = Rule("FREQ=WEEKLY;COUNT=5");

        var first = rule.EndingBefore(Sat3Oct, D(10, 17));
        var rest = rule.StartingFrom(Sat3Oct, D(10, 17));

        Assert.Equal(2, first.Count);
        Assert.Equal(3, rest.Count);
        Assert.Equal([D(10, 17), D(10, 24), D(10, 31)], rest.Dates(D(10, 17), D(10, 1), D(12, 31)));
    }

    [Fact]
    public void Ending_before_a_date_without_a_count_sets_the_last_day()
    {
        var first = Rule("FREQ=WEEKLY").EndingBefore(Sat3Oct, D(10, 17));

        Assert.Equal(D(10, 16), first.Until);
        Assert.Equal([Sat3Oct, D(10, 10)], first.Dates(Sat3Oct, D(10, 1), D(12, 31)));
    }

    [Fact]
    public void Moving_the_start_moves_the_weekdays_with_it()
    {
        // A Saturday series moved to the Monday two days later becomes a Monday series.
        var moved = Rule("FREQ=WEEKLY;BYDAY=SA").MovedTo(Sat3Oct, D(10, 5));

        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", moved.ToString());
    }

    [Fact]
    public void Moving_the_start_of_a_monthly_series_moves_its_day_of_the_month()
    {
        var moved = Rule("FREQ=MONTHLY;BYMONTHDAY=3").MovedTo(Sat3Oct, D(10, 9));

        Assert.Equal("FREQ=MONTHLY;BYMONTHDAY=9", moved.ToString());
    }

    [Fact]
    public void Moving_the_start_of_an_nth_weekday_series_follows_the_new_date()
    {
        // 14 November 2026 is the second Saturday of the month; the 21st is the third.
        var moved = Rule("FREQ=MONTHLY;BYDAY=2SA").MovedTo(D(11, 14), D(11, 21));

        Assert.Equal("FREQ=MONTHLY;BYDAY=3SA", moved.ToString());
    }

    // --- Text form ---------------------------------------------------------------------

    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;UNTIL=20261231")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR;COUNT=12")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1")]
    [InlineData("FREQ=YEARLY;INTERVAL=2")]
    public void The_text_form_reads_back_unchanged(string text)
    {
        Assert.Equal(text, Rule(text).ToString());
    }

    [Fact]
    public void The_RRULE_prefix_and_lower_case_are_accepted()
    {
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO,FR", Rule("rrule:freq=weekly;byday=fr,mo").ToString());
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("INTERVAL=2", "FREQ")]
    [InlineData("FREQ=HOURLY", "not supported")]
    [InlineData("FREQ=DAILY;INTERVAL=0", "interval")]
    [InlineData("FREQ=DAILY;COUNT=0", "number of repeats")]
    [InlineData("FREQ=DAILY;COUNT=2;UNTIL=20261231", "not both")]
    [InlineData("FREQ=DAILY;BYDAY=MO", "weekly")]
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=3", "monthly")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=3;BYDAY=2SA", "not both")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32", "1 to 31")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX", "not a valid day")]
    [InlineData("FREQ=MONTHLY;BYDAY=5SA", "not a valid day")]
    [InlineData("FREQ=DAILY;BYSETPOS=1", "not supported")]
    [InlineData("FREQ=DAILY;UNTIL=soon", "end date")]
    public void A_rule_that_cannot_be_kept_is_refused_with_a_reason(string text, string reason)
    {
        Assert.False(RecurrenceRule.TryParse(text, out _, out var error));
        Assert.Contains(reason, error, StringComparison.OrdinalIgnoreCase);
    }

    // --- Describing --------------------------------------------------------------------

    [Theory]
    [InlineData("FREQ=DAILY", "Daily")]
    [InlineData("FREQ=DAILY;INTERVAL=3", "Every 3 days")]
    [InlineData("FREQ=WEEKLY", "Weekly on Saturday")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,WE", "Weekly on Monday, Wednesday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", "Every 2 weeks on Monday")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "Every weekday")]
    [InlineData("FREQ=MONTHLY", "Monthly on day 3")]
    [InlineData("FREQ=MONTHLY;BYDAY=2SA", "Monthly on the second Saturday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "Monthly on the last Friday")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1", "Monthly on the last day")]
    [InlineData("FREQ=YEARLY", "Annually on October 3")]
    [InlineData("FREQ=DAILY;COUNT=10", "Daily, 10 times")]
    [InlineData("FREQ=DAILY;COUNT=1", "Daily, 1 time")]
    [InlineData("FREQ=WEEKLY;UNTIL=20261231", "Weekly on Saturday, until 31 Dec 2026")]
    public void A_rule_is_described_in_words(string text, string expected)
    {
        Assert.Equal(expected, Rule(text).Describe(Sat3Oct));
    }
}
