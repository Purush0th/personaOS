using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Planner;

/// <summary>
/// Reads a routine pasted as a table ("6:00–6:05  Wake up, make bed", one row a line) into
/// calendar items, in code. Given a 26-row daily routine to put on the calendar until 31 December,
/// qwen2.5:3b proposed tasks on the board, a goal nobody named, at most 16 items at a time, none of
/// them repeating, and "1:10" (after "12:45 Lunch") at ten past one in the morning (production chat,
/// 2026-11-03). Every row is read here: the times as the day runs on, the repeat and its last day
/// from the user's words, and no notification when they asked for none.
/// </summary>
public static partial class ScheduleRequest
{
    /// <summary>A routine needs at least this many rows; a message with two times is a single event.</summary>
    private const int MinimumRows = 3;

    /// <summary>One row: what, from when, until when (24-hour).</summary>
    public sealed record Row(string Title, TimeOnly Start, TimeOnly? End);

    /// <summary>The routine the message holds, and how it repeats.</summary>
    public sealed record Schedule(IReadOnlyList<Row> Rows, string? Repeat, DateOnly? Until, bool NoNotification);

    [GeneratedRegex(
        @"^\s*(?<h1>\d{1,2})[:.](?<m1>\d{2})\s*(?<p1>[ap]\.?m\.?)?\s*(?:[–—-]|to)\s*(?<h2>\d{1,2})[:.](?<m2>\d{2})\s*(?<p2>[ap]\.?m\.?)?[\s|:,–—-]+(?<title>\S.*?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RowLine();

    /// <summary>"* Wake up, make bed at 06:00 AM": one time, after the activity.</summary>
    [GeneratedRegex(
        @"^\s*(?:[*•·-]|\d+[.)])?\s*(?<title>\S.*?)\s+(?:at|@|[–—-])\s*(?<h1>\d{1,2})[:.](?<m1>\d{2})\s*(?<p1>[ap]\.?m\.?)?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TitleThenTime();

    /// <summary>"06:00 AM – Wake up, make bed": one time, before the activity.</summary>
    [GeneratedRegex(
        @"^\s*(?:[*•·-]\s*)?(?<h1>\d{1,2})[:.](?<m1>\d{2})\s*(?<p1>[ap]\.?m\.?)?\s*[|:,–—-]\s*(?<title>[^\d\s].*?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TimeThenTitle();

    [GeneratedRegex(@"\b(?:daily|every\s*day|each\s+day|repeat(?:able|ing|s|ed)?|recurring|routine)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Daily();

    [GeneratedRegex(@"\b(?:week\s*days|every\s+weekday|monday\s+to\s+friday|mon\s*-\s*fri)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Weekdays();

    [GeneratedRegex(
        @"\b(?:till|until|untill|through|thru|to|up\s+to)\s+(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s*(?<day>\d{1,2})(?!\d)(?:st|nd|rd|th)?(?:,?\s*(?<year>20\d\d))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex UntilMonthDay();

    [GeneratedRegex(
        @"\b(?:till|until|untill|through|thru|to|up\s+to)\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?(?:,?\s+(?<year>20\d\d))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex UntilDayMonth();

    [GeneratedRegex(@"\b(?:till|until|untill|through|thru)\s+(?:the\s+)?end\s+of\s+(?:the\s+)?year\b", RegexOptions.IgnoreCase)]
    private static partial Regex UntilYearEnd();

    [GeneratedRegex(@"\b(?:no|without|don'?t\s+(?:need|want)(?:\s+any)?|skip(?:\s+the)?)\s+(?:notifications?|alerts?|reminders?|notify)\b|\bdon'?t\s+notify\b|\bnotifications?\s+(?:not\s+)?(?:required|needed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoAlerts();

    [GeneratedRegex(@"\b(?:not|isn'?t|no)\s+required\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotRequired();

    /// <summary>
    /// The routine in <paramref name="message"/>, or null when it holds fewer than three rows.
    /// <paramref name="context"/> are the user's other recent messages: the repeat, its end and
    /// "no notification" are often said there ("Block my calendar … till Dec 31", then "no
    /// notification required").
    /// </summary>
    public static Schedule? Read(string? message, DateOnly today, IEnumerable<string>? context = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var rows = Rows(message);
        if (rows.Count < MinimumRows) return null;

        var said = string.Join("\n", (context ?? []).Append(message));
        var repeat = Weekdays().IsMatch(said) ? "weekdays" : Daily().IsMatch(said) ? "daily" : null;
        var until = repeat is null ? null : UntilOf(said, today);
        var quiet = NoAlerts().IsMatch(said) || (NotRequired().IsMatch(said) && said.Contains("notif", StringComparison.OrdinalIgnoreCase));
        return new Schedule(rows, repeat, until, quiet);
    }

    /// <summary>Whether a message holds a routine of three or more timed rows.</summary>
    public static bool IsSchedule(string? message) => !string.IsNullOrWhiteSpace(message) && Rows(message).Count >= MinimumRows;

    /// <summary>
    /// The rows, with each time put in the half of the day that keeps the routine running forward:
    /// "1:10" after "12:45" is 13:10. An "am" or "pm" the user wrote always wins.
    /// </summary>
    private static List<Row> Rows(string message)
    {
        var rows = new List<Row>();
        var previous = -1;
        foreach (var line in message.Split('\n'))
        {
            var text = line.Replace('\t', ' ');
            var match = RowLine().Match(text);
            if (!match.Success) match = TitleThenTime().Match(text);
            if (!match.Success) match = TimeThenTitle().Match(text);
            if (!match.Success) continue;

            // "6:00–6:05 AM": the end's am/pm covers the start too, unless the range crosses noon
            // ("12:45–1:10 PM", "11:30–12:30 PM"), where the start is read as the day runs on.
            var suffix = match.Groups["p1"].Value;
            if (suffix.Length == 0 && match.Groups["p2"].Value.Length > 0
                && int.Parse(match.Groups["h1"].Value, CultureInfo.InvariantCulture) % 12 <= int.Parse(match.Groups["h2"].Value, CultureInfo.InvariantCulture) % 12)
            {
                suffix = match.Groups["p2"].Value;
            }
            var start = Minutes(match.Groups["h1"].Value, match.Groups["m1"].Value, suffix, previous);
            if (start is null) continue;
            var end = match.Groups["h2"].Success
                ? Minutes(match.Groups["h2"].Value, match.Groups["m2"].Value, match.Groups["p2"].Value, start.Value)
                : null;

            var title = match.Groups["title"].Value.Trim().TrimEnd('.', ';');
            if (title.Length == 0) continue;
            previous = start.Value;
            rows.Add(new Row(title, Time(start.Value), end is int e && e > start.Value && e < 24 * 60 ? Time(e) : null));
        }

        // A list of start times only: each row runs until the next one starts.
        for (var i = 0; i < rows.Count - 1; i++)
        {
            if (rows[i].End is null && rows[i + 1].Start > rows[i].Start) rows[i] = rows[i] with { End = rows[i + 1].Start };
        }
        return rows;
    }

    /// <summary>Minutes after midnight, the first reading not before <paramref name="after"/> when no am/pm says which.</summary>
    private static int? Minutes(string hourText, string minuteText, string suffix, int after)
    {
        var hour = int.Parse(hourText, CultureInfo.InvariantCulture);
        var minute = int.Parse(minuteText, CultureInfo.InvariantCulture);
        if (minute > 59 || hour > 23) return null;

        if (suffix.Length > 0)
        {
            if (hour is 0 or > 12) return null;
            var pm = char.ToLowerInvariant(suffix[0]) == 'p';
            return ((hour % 12) + (pm ? 12 : 0)) * 60 + minute;
        }

        if (hour > 12) return hour * 60 + minute;
        var morning = (hour % 12) * 60 + minute;
        if (hour == 12) morning = 12 * 60 + minute;
        if (morning >= after) return morning;
        var evening = morning + 12 * 60;
        return evening < 24 * 60 && evening >= after ? evening : morning;
    }

    private static TimeOnly Time(int minutes) => new(minutes / 60, minutes % 60);

    private static DateOnly? UntilOf(string text, DateOnly today)
    {
        if (UntilYearEnd().IsMatch(text)) return new DateOnly(today.Year, 12, 31);
        var match = UntilMonthDay().Match(text);
        if (!match.Success) match = UntilDayMonth().Match(text);
        if (!match.Success) return null;

        var month = DateTime.ParseExact(match.Groups["month"].Value, "MMM", CultureInfo.InvariantCulture).Month;
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture) : today.Year;
        if (day > DateTime.DaysInMonth(year, month)) return null;
        var date = new DateOnly(year, month, day);
        return !match.Groups["year"].Success && date < today ? date.AddYears(1) : date;
    }
}
