using System.Globalization;
using System.Text;

namespace PersonaOS.Domain.Services;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// How an event repeats: the subset of RFC 5545 (iCalendar) RRULE that a calendar's "repeat" menu
/// can express, stored as the standard text (<c>FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;UNTIL=20261231</c>)
/// so it can be read, exported or replaced by a fuller implementation without a data migration.
///
/// Supported: FREQ (daily, weekly, monthly, yearly), INTERVAL, BYDAY (weekly: any days; monthly: one
/// weekday with an ordinal, like <c>2SA</c> or <c>-1FR</c>), BYMONTHDAY (monthly, <c>-1</c> is the last
/// day), COUNT or UNTIL (not both). Dates only: an event's time of day belongs to the event, and
/// weeks start on Monday.
///
/// The rule never stores its occurrences; <see cref="Dates"/> works them out for a window.
/// A month that lacks the day (the 31st, a fifth Monday) is skipped, as Google Calendar does.
/// </summary>
public sealed class RecurrenceRule
{
    public const int MaxInterval = 999;
    public const int MaxCount = 999;

    /// <summary>Safety valve: a rule that would walk further than this many periods is refused.</summary>
    private const int MaxPeriods = 100_000;

    private static readonly string[] DayCodes = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    public RecurrenceFrequency Frequency { get; init; }

    public int Interval { get; init; } = 1;

    /// <summary>Weekly: the days it happens on. Empty means the weekday of the first occurrence.</summary>
    public IReadOnlyList<DayOfWeek> ByDay { get; init; } = [];

    /// <summary>Monthly: day of the month (1 to 31), or -1 for the last day.</summary>
    public int? ByMonthDay { get; init; }

    /// <summary>Monthly: which one, 1 to 4 or -1 for the last, paired with <see cref="NthWeekday"/>.</summary>
    public int? NthOrdinal { get; init; }

    public DayOfWeek? NthWeekday { get; init; }

    /// <summary>Total number of occurrences, counted from the first. Null with no <see cref="Until"/> means forever.</summary>
    public int? Count { get; init; }

    /// <summary>The last day an occurrence may fall on.</summary>
    public DateOnly? Until { get; init; }

    // --- Text form -------------------------------------------------------------------------

    /// <summary>Reads the text form; <paramref name="error"/> is presentable to the user.</summary>
    public static bool TryParse(string? text, out RecurrenceRule rule, out string error)
    {
        rule = new RecurrenceRule();
        error = string.Empty;

        var body = (text ?? string.Empty).Trim();
        if (body.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) body = body[6..];
        if (body.Length == 0)
        {
            error = "The repeat rule is empty.";
            return false;
        }

        RecurrenceFrequency? frequency = null;
        var interval = 1;
        var byDay = new List<DayOfWeek>();
        int? byMonthDay = null, ordinal = null, count = null;
        DayOfWeek? nthWeekday = null;
        DateOnly? until = null;

        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || pair[1].Length == 0)
            {
                error = $"'{part}' is not a valid part of a repeat rule.";
                return false;
            }

            var value = pair[1];
            switch (pair[0].ToUpperInvariant())
            {
                case "FREQ":
                    frequency = value.ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };
                    if (frequency is null)
                    {
                        error = $"Repeat '{value}' is not supported: use DAILY, WEEKLY, MONTHLY or YEARLY.";
                        return false;
                    }

                    break;
                case "INTERVAL":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out interval)
                        || interval is < 1 or > MaxInterval)
                    {
                        error = $"The repeat interval must be a whole number from 1 to {MaxInterval}.";
                        return false;
                    }

                    break;
                case "COUNT":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var c)
                        || c is < 1 or > MaxCount)
                    {
                        error = $"The number of repeats must be a whole number from 1 to {MaxCount}.";
                        return false;
                    }

                    count = c;
                    break;
                case "UNTIL":
                    if (!TryParseDate(value, out var untilDate))
                    {
                        error = $"'{value}' is not a valid end date for a repeat (use yyyyMMdd).";
                        return false;
                    }

                    until = untilDate;
                    break;
                case "BYDAY":
                    foreach (var token in value.Split(',', StringSplitOptions.TrimEntries))
                    {
                        if (!TryParseDay(token, out var day, out var nth))
                        {
                            error = $"'{token}' is not a valid day for a repeat (use MO, TU, WE, TH, FR, SA, SU).";
                            return false;
                        }

                        if (nth is not null)
                        {
                            ordinal = nth;
                            nthWeekday = day;
                        }
                        else
                        {
                            byDay.Add(day);
                        }
                    }

                    break;
                case "BYMONTHDAY":
                    if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var d)
                        || d == 0 || d is < -1 or > 31)
                    {
                        error = "The day of the month must be 1 to 31, or -1 for the last day.";
                        return false;
                    }

                    byMonthDay = d;
                    break;
                default:
                    error = $"'{pair[0]}' is not supported in a repeat rule.";
                    return false;
            }
        }

        if (frequency is null)
        {
            error = "A repeat rule needs FREQ (DAILY, WEEKLY, MONTHLY or YEARLY).";
            return false;
        }

        if (count is not null && until is not null)
        {
            error = "A repeat can end after a number of times or on a date, not both.";
            return false;
        }

        if (byDay.Count > 0 && frequency != RecurrenceFrequency.Weekly)
        {
            error = "Choosing days of the week only works for a weekly repeat.";
            return false;
        }

        if ((byMonthDay is not null || ordinal is not null) && frequency != RecurrenceFrequency.Monthly)
        {
            error = "A day of the month only works for a monthly repeat.";
            return false;
        }

        if (byMonthDay is not null && ordinal is not null)
        {
            error = "A monthly repeat is on a day of the month or on a weekday, not both.";
            return false;
        }

        rule = new RecurrenceRule
        {
            Frequency = frequency.Value,
            Interval = interval,
            ByDay = byDay.Distinct().OrderBy(DayOffset).ToList(),
            ByMonthDay = byMonthDay,
            NthOrdinal = ordinal,
            NthWeekday = nthWeekday,
            Count = count,
            Until = until,
        };
        return true;
    }

    /// <summary>The canonical text form, readable back by <see cref="TryParse"/>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder("FREQ=").Append(Frequency.ToString().ToUpperInvariant());
        if (Interval > 1) sb.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        if (ByDay.Count > 0) sb.Append(";BYDAY=").Append(string.Join(',', ByDay.OrderBy(DayOffset).Select(Code)));
        if (NthOrdinal is int n && NthWeekday is DayOfWeek w)
            sb.Append(";BYDAY=").Append(n.ToString(CultureInfo.InvariantCulture)).Append(Code(w));
        if (ByMonthDay is int m) sb.Append(";BYMONTHDAY=").Append(m.ToString(CultureInfo.InvariantCulture));
        if (Count is int c) sb.Append(";COUNT=").Append(c.ToString(CultureInfo.InvariantCulture));
        if (Until is DateOnly u) sb.Append(";UNTIL=").Append(u.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // --- Occurrences -----------------------------------------------------------------------

    /// <summary>
    /// The days the event falls on within <paramref name="from"/> to <paramref name="to"/>
    /// (inclusive), in order, for a series whose first occurrence is on <paramref name="start"/>.
    /// </summary>
    public IEnumerable<DateOnly> Dates(DateOnly start, DateOnly from, DateOnly to)
    {
        if (to < from || to < start) yield break;

        var emitted = 0;
        // A count has to be counted from the first occurrence; without one, jump near the window.
        var period = Count is null ? FirstPeriodNear(start, from) : 0;

        for (var walked = 0; walked < MaxPeriods; walked++, period++)
        {
            // Nothing in this period or any later one can fall inside the window.
            if (PeriodStart(start, period) is not DateOnly periodStart || periodStart > to) yield break;
            if (DatesInPeriod(start, period) is not { } dates) yield break;

            foreach (var date in dates)
            {
                if (date < start) continue;
                if (Until is DateOnly until && date > until) yield break;
                if (Count is int count && emitted >= count) yield break;

                emitted++;
                if (date > to) yield break;
                if (date >= from) yield return date;
            }
        }

        throw new InvalidOperationException("The repeat rule runs for too many periods to expand.");
    }

    /// <summary>How many occurrences fall before <paramref name="date"/>.</summary>
    public int CountBefore(DateOnly start, DateOnly date) =>
        date <= start ? 0 : Dates(start, start, date.AddDays(-1)).Count();

    /// <summary>The same rule, ending the day before <paramref name="date"/>: the first half of a split series.</summary>
    public RecurrenceRule EndingBefore(DateOnly start, DateOnly date) => Count is null
        ? Copy(count: null, until: date.AddDays(-1))
        : Copy(count: CountBefore(start, date), until: null);

    /// <summary>The rest of a split series, from <paramref name="date"/> on: its count shrinks by what was used.</summary>
    public RecurrenceRule StartingFrom(DateOnly start, DateOnly date) => Count is int count
        ? Copy(count: Math.Max(1, count - CountBefore(start, date)), until: null)
        : this;

    /// <summary>
    /// The rule for a series whose first occurrence moved from <paramref name="oldStart"/> to
    /// <paramref name="newStart"/>: weekdays and the day of the month follow the move.
    /// </summary>
    public RecurrenceRule MovedTo(DateOnly oldStart, DateOnly newStart)
    {
        var delta = newStart.DayNumber - oldStart.DayNumber;
        if (delta == 0) return this;

        return new RecurrenceRule
        {
            Frequency = Frequency,
            Interval = Interval,
            ByDay = ByDay.Select(d => (DayOfWeek)(((int)d + delta % 7 + 7) % 7)).Distinct().OrderBy(DayOffset).ToList(),
            ByMonthDay = ByMonthDay is null ? null : ByMonthDay == -1 ? -1 : newStart.Day,
            NthOrdinal = NthOrdinal is null ? null : OrdinalOf(newStart, NthOrdinal == -1),
            NthWeekday = NthOrdinal is null ? null : newStart.DayOfWeek,
            Count = Count,
            Until = Until,
        };
    }

    // --- Description -----------------------------------------------------------------------

    /// <summary>"Weekly on Saturday", "Monthly on the second Saturday, 10 times": for the repeat picker and the event.</summary>
    public string Describe(DateOnly start)
    {
        var text = Frequency switch
        {
            RecurrenceFrequency.Daily => Interval == 1 ? "Daily" : $"Every {Interval} days",
            RecurrenceFrequency.Weekly => DescribeWeekly(start),
            RecurrenceFrequency.Monthly => DescribeMonthly(start),
            _ => Interval == 1
                ? $"Annually on {start.ToString("MMMM d", CultureInfo.InvariantCulture)}"
                : $"Every {Interval} years on {start.ToString("MMMM d", CultureInfo.InvariantCulture)}",
        };

        if (Count is int count) return $"{text}, {count} time{(count == 1 ? string.Empty : "s")}";
        if (Until is DateOnly until) return $"{text}, until {until.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        return text;
    }

    private string DescribeWeekly(DateOnly start)
    {
        var days = ByDay.Count > 0 ? ByDay : [start.DayOfWeek];
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (Interval == 1 && days.Count == 5 && weekdays.All(days.Contains)) return "Every weekday";

        var names = string.Join(", ", days.OrderBy(DayOffset).Select(d => d.ToString()));
        return Interval == 1 ? $"Weekly on {names}" : $"Every {Interval} weeks on {names}";
    }

    private string DescribeMonthly(DateOnly start)
    {
        var every = Interval == 1 ? "Monthly" : $"Every {Interval} months";
        if (NthOrdinal is int n && NthWeekday is DayOfWeek w) return $"{every} on the {OrdinalWord(n)} {w}";
        return ByMonthDay switch
        {
            -1 => $"{every} on the last day",
            int day => $"{every} on day {day}",
            _ => $"{every} on day {start.Day}",
        };
    }

    // --- Internals -------------------------------------------------------------------------

    private RecurrenceRule Copy(int? count, DateOnly? until) => new()
    {
        Frequency = Frequency,
        Interval = Interval,
        ByDay = ByDay,
        ByMonthDay = ByMonthDay,
        NthOrdinal = NthOrdinal,
        NthWeekday = NthWeekday,
        Count = count,
        Until = until,
    };

    /// <summary>The dates one period (a day, week, month or year step) produces, or null past the end of time.</summary>
    private List<DateOnly>? DatesInPeriod(DateOnly start, int period)
    {
        try
        {
            switch (Frequency)
            {
                case RecurrenceFrequency.Daily:
                    return [start.AddDays(checked(period * Interval))];

                case RecurrenceFrequency.Weekly:
                    var weekStart = MondayOnOrBefore(start).AddDays(checked(7 * Interval * period));
                    var days = ByDay.Count > 0 ? ByDay : [start.DayOfWeek];
                    return days.OrderBy(DayOffset).Select(d => weekStart.AddDays(DayOffset(d))).ToList();

                case RecurrenceFrequency.Monthly:
                    var months = start.Year * 12 + (start.Month - 1) + checked(period * Interval);
                    return MonthlyDate(start, months / 12, months % 12 + 1) is DateOnly monthly ? [monthly] : [];

                default:
                    var year = start.Year + checked(period * Interval);
                    return start.Day <= DateTime.DaysInMonth(year, start.Month)
                        ? [new DateOnly(year, start.Month, start.Day)]
                        : [];
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private DateOnly? MonthlyDate(DateOnly start, int year, int month)
    {
        var length = DateTime.DaysInMonth(year, month);

        if (NthOrdinal is int n && NthWeekday is DayOfWeek weekday)
        {
            if (n == -1)
            {
                var last = new DateOnly(year, month, length);
                return last.AddDays(-(((int)last.DayOfWeek - (int)weekday + 7) % 7));
            }

            var first = new DateOnly(year, month, 1);
            var day = 1 + ((int)weekday - (int)first.DayOfWeek + 7) % 7 + 7 * (n - 1);
            return day <= length ? new DateOnly(year, month, day) : null;
        }

        var wanted = ByMonthDay ?? start.Day;
        if (wanted == -1) return new DateOnly(year, month, length);
        return wanted <= length ? new DateOnly(year, month, wanted) : null;
    }

    /// <summary>The first day a period can produce a date on (a month starts on its 1st), or null past the end of time.</summary>
    private DateOnly? PeriodStart(DateOnly start, int period)
    {
        try
        {
            switch (Frequency)
            {
                case RecurrenceFrequency.Daily:
                    return start.AddDays(checked(period * Interval));
                case RecurrenceFrequency.Weekly:
                    return MondayOnOrBefore(start).AddDays(checked(7 * Interval * period));
                case RecurrenceFrequency.Monthly:
                    var months = start.Year * 12 + (start.Month - 1) + checked(period * Interval);
                    return new DateOnly(months / 12, months % 12 + 1, 1);
                default:
                    return new DateOnly(start.Year + checked(period * Interval), 1, 1);
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>A period safely at or before the window start, so a far-off window is not walked from the beginning.</summary>
    private int FirstPeriodNear(DateOnly start, DateOnly from)
    {
        if (from <= start) return 0;

        var span = Frequency switch
        {
            RecurrenceFrequency.Daily => from.DayNumber - start.DayNumber,
            RecurrenceFrequency.Weekly => (from.DayNumber - MondayOnOrBefore(start).DayNumber) / 7,
            RecurrenceFrequency.Monthly => (from.Year - start.Year) * 12 + from.Month - start.Month,
            _ => from.Year - start.Year,
        };
        return Math.Max(0, span / Interval - 1);
    }

    private static DateOnly MondayOnOrBefore(DateOnly date) => date.AddDays(-DayOffset(date.DayOfWeek));

    /// <summary>Days after Monday: Monday 0 to Sunday 6.</summary>
    private static int DayOffset(DayOfWeek day) => ((int)day + 6) % 7;

    private static string Code(DayOfWeek day) => DayCodes[DayOffset(day)];

    private static int OrdinalOf(DateOnly date, bool preferLast)
    {
        var isLast = date.AddDays(7).Month != date.Month;
        return preferLast && isLast ? -1 : Math.Min(4, (date.Day - 1) / 7 + 1);
    }

    private static string OrdinalWord(int n) => n switch
    {
        -1 => "last",
        1 => "first",
        2 => "second",
        3 => "third",
        _ => "fourth",
    };

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        || (text.Length > 8 && text[8] == 'T'
            && DateOnly.TryParseExact(text[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date));

    /// <summary>"MO", "2TU" or "-1FR": a weekday, with the ordinal when there is one.</summary>
    private static bool TryParseDay(string token, out DayOfWeek day, out int? ordinal)
    {
        day = DayOfWeek.Monday;
        ordinal = null;
        var code = token.Length >= 2 ? token[^2..].ToUpperInvariant() : string.Empty;
        var index = Array.IndexOf(DayCodes, code);
        if (index < 0) return false;

        day = (DayOfWeek)((index + 1) % 7);
        var prefix = token[..^2];
        if (prefix.Length == 0) return true;

        if (!int.TryParse(prefix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            || n is not (1 or 2 or 3 or 4 or -1))
            return false;

        ordinal = n;
        return true;
    }
}
