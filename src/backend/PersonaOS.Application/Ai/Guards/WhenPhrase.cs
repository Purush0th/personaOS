using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// When a message says something should happen, read the way a person means it: "in 2 hours",
/// "on Thursday at 10am", "tomorrow at 9", "at 7pm". Small models get this arithmetic wrong with
/// the week listed in front of them: qwen2.5:3b put "in 2 hours" at 11:00 when it was 09:18, and
/// "Thursday" on the Friday, three tries in three (accuracy suite, 2026-11-02). Reading it in code
/// makes it exact.
///
/// Only what is certain is returned. "next Friday" (this week's or the next?), "at 9" (morning or
/// evening?), two days or two times in one message, and a weekday that is today are left out, and
/// the model's own reading stands for them.
/// </summary>
public static partial class WhenPhrase
{
    /// <summary>What a message pins down: a day, a time of day, or an exact moment ("in 2 hours").</summary>
    public sealed record Reading(DateOnly? Day, TimeOnly? Time, string DayWords, string TimeWords)
    {
        /// <summary>The day and time together, when the message gave both.</summary>
        public DateTime? Moment => Day is DateOnly d && Time is TimeOnly t ? d.ToDateTime(t) : null;
    }

    [GeneratedRegex(@"\bin\s+(?<n>\d+|an?|one|two|three|four|five|ten|fifteen|twenty|thirty|forty-five|half\s+an?)\s+(?<unit>min(?:ute)?s?|hours?|hrs?|days?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    [GeneratedRegex(@"\b(?<word>today|tonight|tomorrow|day\s+after\s+tomorrow|this\s+(?:morning|afternoon|evening))\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDay();

    [GeneratedRegex(@"\b(?<next>next\s+)?(?:this\s+|on\s+|by\s+|coming\s+)?(?<day>monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Weekday();

    /// <summary>Anything else that names a day: a date, a month, "next week", "in 3 days" is read by <see cref="Offset"/>.</summary>
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}|\b\d{1,2}[/.]\d{1,2}\b|\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\b|\bmay\s+\d|\b\d{1,2}(?:st|nd|rd|th)\b|\bnext\s+(?:week|month|year)\b|\byesterday\b|\bweekend\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherDay();

    /// <summary>
    /// "3pm", "3 pm", "3:30pm", "15:00", "09:30", "noon", "midnight". A bare "at 9" or "3:30" is not a
    /// time: morning or afternoon?
    /// </summary>
    [GeneratedRegex(@"\b(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ampm>a\.?m\.?|p\.?m\.?)(?![a-z])|\b(?<h24>[01]\d|2[0-3]):(?<m24>[0-5]\d)\b|\b(?<named>noon|midday|midnight)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClockTime();

    /// <summary>
    /// "at 9", "8:15": a time with no am or pm. Only read when <see cref="HalfOfDay"/> says which half
    /// (holdout2: "Ping me at 8:15 tonight" became a card for 21:15, then for 08:15, 2026-11-06).
    /// </summary>
    [GeneratedRegex(@"(?:\bat\s+(?<h>\d{1,2})(?::(?<m>[0-5]\d))?|(?<![\d:.])(?<h>\d{1,2}):(?<m>[0-5]\d))(?![\d:]|\s*[ap]\.?m)", RegexOptions.IgnoreCase)]
    private static partial Regex BareTime();

    [GeneratedRegex(@"\b(?:tonight|(?:this|in\s+the)\s+(?:morning|afternoon|evening)|at\s+night)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HalfOfDay();

    /// <summary>A part of the day, which says roughly when without a clock time: enough not to have to ask.</summary>
    [GeneratedRegex(@"\b(?:tonight|morning|afternoon|evening|night|noon|midday|midnight|lunch(?:time)?|dinner(?:time)?|bedtime|later\s+today|end\s+of\s+(?:the\s+)?day|eod)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayPart();

    /// <summary>
    /// Whether the message says when at all: a clock time, a part of the day, or "in 20 minutes".
    /// A day alone ("tomorrow") does not say when a reminder should go off.
    /// </summary>
    public static bool SaysWhen(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (ClockTime().IsMatch(message) || DayPart().IsMatch(message) || Offset().IsMatch(message) || BareHour().IsMatch(message));

    /// <summary>"at 9": a time, if not which 9. The model picks one and the card shows it.</summary>
    [GeneratedRegex(@"\bat\s+\d{1,2}(?::\d{2})?\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareHour();

    /// <summary>
    /// A time, or today, tonight or tomorrow, at the very end of a text, with the word that leads into
    /// it ("at 7pm", "in 20 minutes"). Not a weekday or a bare number: "Prepare for Monday" and "Read
    /// chapter 12" say what the reminder is about.
    /// </summary>
    [GeneratedRegex(@"(?:[\s,]+(?:(?:at|by|this|in|for)\s+)?(?:\d{1,2}(?::\d{2})?\s*(?:a\.?m\.?|p\.?m\.?)|(?:[01]\d|2[0-3]):[0-5]\d|noon|midday|midnight|today|tonight|tomorrow|day\s+after\s+tomorrow|(?:the\s+)?(?:morning|afternoon|evening)|\d+\s+(?:min(?:ute)?s?|hours?|hrs?)|an?\s+hour|half\s+an\s+hour)|[\s,]+at\s+\d{1,2}(?::\d{2})?)[\s.!]*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingWhen();

    /// <summary>
    /// <paramref name="text"/> without the time at its end: "Water the ferns at 7pm tomorrow" is
    /// "Water the ferns". Left as it is when nothing else would remain.
    /// </summary>
    public static string WithoutTrailingWhen(string text)
    {
        var rest = text;
        for (var match = TrailingWhen().Match(rest); match.Success && match.Index > 0; match = TrailingWhen().Match(rest))
            rest = rest[..match.Index];
        return rest.Count(char.IsLetter) >= 3 ? rest.TrimEnd() : text;
    }

    /// <summary>What <paramref name="message"/> pins down, said at <paramref name="now"/> (the user's local time).</summary>
    public static Reading? Read(string? message, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        // "in 2 hours" is a moment of its own; with any other day or time beside it, nothing is certain.
        var offsets = Offset().Matches(message);
        if (offsets.Count > 1) return null;
        if (offsets.Count == 1)
        {
            if (RelativeDay().IsMatch(message) || Weekday().IsMatch(message) || OtherDay().IsMatch(message) || ClockTime().IsMatch(message))
                return null;
            var span = SpanOf(offsets[0]);
            if (span is null) return null;
            var at = now.AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond) + span.Value;
            return new Reading(DateOnly.FromDateTime(at), TimeOnly.FromDateTime(at), offsets[0].Value, offsets[0].Value);
        }

        var today = DateOnly.FromDateTime(now);
        DateOnly? day = null;
        var dayWords = string.Empty;
        if (!OtherDay().IsMatch(message))
        {
            var days = new List<(DateOnly Day, string Words)>();
            foreach (Match m in RelativeDay().Matches(message))
            {
                var word = Regex.Replace(m.Groups["word"].Value.ToLowerInvariant(), @"\s+", " ");
                days.Add((word switch
                {
                    "tomorrow" => today.AddDays(1),
                    "day after tomorrow" => today.AddDays(2),
                    _ => today,
                }, word));
            }
            // "the day after tomorrow" also matched "tomorrow".
            if (days.Any(d => d.Words == "day after tomorrow")) days.RemoveAll(d => d.Words == "tomorrow");

            var weekdayAmbiguous = false;
            foreach (Match m in Weekday().Matches(message))
            {
                var weekday = Enum.Parse<DayOfWeek>(m.Groups["day"].Value, ignoreCase: true);
                // "next Friday" and a weekday that is today are read differently by different people.
                if (m.Groups["next"].Success || weekday == today.DayOfWeek)
                {
                    weekdayAmbiguous = true;
                    continue;
                }
                var ahead = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
                days.Add((today.AddDays(ahead), m.Groups["day"].Value.ToLowerInvariant()));
            }

            var distinct = days.Select(d => d.Day).Distinct().ToList();
            if (distinct.Count == 1 && !weekdayAmbiguous)
            {
                day = distinct[0];
                dayWords = days[0].Words;
            }
            else if (distinct.Count > 1 || weekdayAmbiguous)
            {
                return null;
            }
        }

        TimeOnly? time = null;
        var timeWords = string.Empty;
        var clocks = ClockTime().Matches(message);
        if (clocks.Count == 1 && TimeOf(clocks[0]) is TimeOnly t)
        {
            time = t;
            timeWords = clocks[0].Value;
        }
        else if (clocks.Count > 1)
        {
            // "from 3pm to 4pm" names a start and an end: the start is the time it happens.
            if (TimeOf(clocks[0]) is TimeOnly start && !Regex.IsMatch(message, @"\b(?:or|and)\b", RegexOptions.IgnoreCase))
            {
                time = start;
                timeWords = clocks[0].Value;
            }
        }

        // "8:15 tonight", "at 9 this evening": a bare time, and the part of the day says which half.
        if (time is null && clocks.Count == 0 && HalfOfDay().Match(message) is { Success: true } half
            && BareTime().Matches(message) is { Count: 1 } bare)
        {
            var hour = int.Parse(bare[0].Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = bare[0].Groups["m"].Success ? int.Parse(bare[0].Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (hour is >= 1 and <= 12)
            {
                var later = !half.Value.Contains("morning", StringComparison.OrdinalIgnoreCase);
                time = new TimeOnly(hour % 12 + (later ? 12 : 0), minute);
                timeWords = bare[0].Value;
            }
        }

        return day is null && time is null ? null : new Reading(day, time, dayWords, timeWords);
    }

    private static TimeSpan? SpanOf(Match offset)
    {
        var n = offset.Groups["n"].Value.ToLowerInvariant();
        double amount = n switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "ten" => 10,
            "fifteen" => 15,
            "twenty" => 20,
            "thirty" => 30,
            "forty-five" => 45,
            _ when n.StartsWith("half", StringComparison.Ordinal) => 0.5,
            _ => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1,
        };
        if (amount <= 0) return null;
        var unit = offset.Groups["unit"].Value.ToLowerInvariant();
        // "in half a minute" is not a reminder anyone sets; half only goes with hours.
        if (amount < 1 && !unit.StartsWith('h')) return null;
        return unit[0] switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount),
        };
    }

    private static TimeOnly? TimeOf(Match clock)
    {
        if (clock.Groups["named"].Success)
            return clock.Groups["named"].Value.Equals("midnight", StringComparison.OrdinalIgnoreCase) ? new TimeOnly(0, 0) : new TimeOnly(12, 0);

        if (clock.Groups["h24"].Success)
            return new TimeOnly(int.Parse(clock.Groups["h24"].Value, CultureInfo.InvariantCulture), int.Parse(clock.Groups["m24"].Value, CultureInfo.InvariantCulture));

        var hour = int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = clock.Groups["m"].Success ? int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour is < 1 or > 12 || minute > 59) return null;
        var pm = clock.Groups["ampm"].Value.StartsWith('p') || clock.Groups["ampm"].Value.StartsWith('P');
        return new TimeOnly(hour % 12 + (pm ? 12 : 0), minute);
    }
}
