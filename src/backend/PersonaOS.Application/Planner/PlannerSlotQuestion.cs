using System.Globalization;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common;

namespace PersonaOS.Application.Planner;

/// <summary>
/// Questions about one time, a stretch of the day or a count, which have one right answer in the
/// day's items: "What's in my day at 7:20pm?", "What happens from 3 to 5pm today?", "How many walks
/// are on today's plan?". Stock and fine-tuned qwen3:4b both said nothing was on at 7:20 inside an
/// 18:30 to 19:45 gym session, named the 13:00 calls for 3 to 5pm and counted one walk of two
/// (planner suite, 2026-11-07). The answers follow training/planner_labels.py, so the app and the
/// training labels say the same thing.
/// </summary>
public static partial class PlannerSlotQuestion
{
    public abstract record Question(DateOnly? Day);

    /// <summary>What is on at one time.</summary>
    public sealed record At(TimeOnly Time, DateOnly? Day) : Question(Day);

    /// <summary>What overlaps a stretch of the day.</summary>
    public sealed record Between(TimeOnly From, TimeOnly To, DateOnly? Day) : Question(Day);

    /// <summary>How many items are about a word; a null word counts them all.</summary>
    public sealed record Count(string? Word, string Many, DateOnly? Day) : Question(Day);

    private const string Clock =
        @"(?:(?:half|quarter)\s+past\s+(?:\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)"
        + @"|\d{1,2}(?::[0-5]\d)?(?:\s*[ap]\.?\s?m\b\.?)?|noon|midday|midnight)"
        + @"(?:\s+(?:in\s+the\s+(?:morning|afternoon|evening)|at\s+night|tonight|this\s+(?:morning|afternoon|evening)))?";

    [GeneratedRegex(@"(?<![\w:])(?<t>" + Clock + @")(?![\w:])", RegexOptions.IgnoreCase)]
    private static partial Regex Time();

    [GeneratedRegex(
        @"(?:\b(?:between|from)\s+)?(?<![\w:])(?<a>" + Clock + @")\s*(?:and|to|until|till|through|-|–)\s*(?<b>" + Clock + @")(?![\w:])",
        RegexOptions.IgnoreCase)]
    private static partial Regex Range();

    [GeneratedRegex(@"\bhow\s+many\s+(?<w>[a-z][a-z'-]*)|\bcount\s+(?:up\s+)?(?:my|the|all\s+(?:my|the))?\s*(?<w>[a-z][a-z'-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex HowMany();

    /// <summary>The message asks what is there, not for a change.</summary>
    [GeneratedRegex(
        @"\bwhat(?:'s|’s|s|\s+is|\s+am\s+i|\s+do\s+i|\s+happens|\s+fills|\s+have\s+i)?\b|\btell\s+me\b|\bwalk\s+me\s+through\b|\bshow\s+me\b|\bslot\s+for\b|\bwhich\b|\?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksWhat();

    [GeneratedRegex(
        @"\b(?:add|schedule\s+(?:a|an|my)|book|create|move|reschedule|cancel|delete|remove|remind|set\s+(?:a|an|up)|put|change|mark|plan\s+(?:a|an|my)|block|pencil|squeeze|pop|jot|into|onto)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksForAChange();

    /// <summary>Questions about another part of the app or a reminder's time.</summary>
    [GeneratedRegex(@"\b(?:reminders?|notes?|tasks?|TASK-\d+|sprints?|board|backlog|goals?|GOAL-\d+|memor(?:y|ies)|alarm)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherPart();

    [GeneratedRegex(@"\b(?:in\s+\d+|in\s+(?:an?|half\s+an?)\s+hour|for\s+\d+\s*(?:min|hour))", RegexOptions.IgnoreCase)]
    private static partial Regex Duration();

    /// <summary>Arithmetic, whose numbers are not times.</summary>
    [GeneratedRegex(@"%|\bper\s*cent\b|\b(?:times|divided|plus|minus|multiplied|squared|square\s+root)\b|\d\s*[+*/×]\s*\d", RegexOptions.IgnoreCase)]
    private static partial Regex Sum();

    [GeneratedRegex(@"\b(?:today|tomorrow|tonight|my\s+day|(?:my|the|today's)\s+(?:plan|planner|schedule|calendar|diary|agenda)|plan|planner|schedule|calendar|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AboutTheDay();

    /// <summary>Words that name every item, not a kind of item.</summary>
    private static readonly HashSet<string> Everything = new(StringComparer.OrdinalIgnoreCase)
    {
        "items", "item", "things", "events", "event", "slots", "blocks", "entries", "activities", "of",
    };

    /// <summary>The question, or null when the message is not one of these.</summary>
    public static Question? Read(string? message, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(message) || AsksForAChange().IsMatch(message) || OtherPart().IsMatch(message)
            || Duration().IsMatch(message) || Sum().IsMatch(message))
            return null;
        var day = Ai.Guards.WhenPhrase.Read(message, now)?.Day;

        if (HowMany().Match(message) is { Success: true } count)
        {
            // "How many minutes are in a day?" is not about the planner.
            if (!AboutTheDay().IsMatch(message)) return null;
            var word = count.Groups["w"].Value.ToLowerInvariant();
            return Everything.Contains(word) ? new Count(null, "items", day) : new Count(word, word, day);
        }

        if (!AsksWhat().IsMatch(message)) return null;

        if (Range().Match(message) is { Success: true } range)
        {
            var b = TimeOf(range.Groups["b"].Value, null);
            var a = TimeOf(range.Groups["a"].Value, b is { } end ? Half(range.Groups["b"].Value, end) : null);
            if (a is TimeOnly from && b is TimeOnly to && to > from) return new Between(from, to, day);
            return null;
        }

        // A bare number is a time only after "at" ("at 9"): "20% of 250" names none.
        var times = Time().Matches(message)
            .Where(m => !Regex.IsMatch(m.Groups["t"].Value, @"^\d{1,2}$") || Regex.IsMatch(message[..m.Index], @"\bat\s+$", RegexOptions.IgnoreCase))
            .Select(m => TimeOf(m.Groups["t"].Value, null))
            .ToList();
        if (times.Count == 1 && times[0] is TimeOnly at) return new At(at, day);
        return null;
    }

    /// <summary>Whether a time with an explicit half of the day is in the afternoon; null when it says neither.</summary>
    private static bool? Half(string text, TimeOnly read)
    {
        var t = text.ToLowerInvariant();
        if (Regex.IsMatch(t, @"[ap]\.?\s?m|morning|afternoon|evening|night|tonight|noon|midday|midnight"))
            return read.Hour >= 12;
        return null;
    }

    private static readonly string[] Numbers = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>
    /// The time a phrase names. A bare hour takes the half of the day <paramref name="pm"/> gives (the
    /// end of a range: "3 to 5pm"), or else from 1 to 5 the afternoon and otherwise as written.
    /// </summary>
    private static TimeOnly? TimeOf(string phrase, bool? pm)
    {
        var t = phrase.Trim().ToLowerInvariant();
        if (t.StartsWith("noon", StringComparison.Ordinal) || t.StartsWith("midday", StringComparison.Ordinal)) return new TimeOnly(12, 0);
        if (t.StartsWith("midnight", StringComparison.Ordinal)) return new TimeOnly(0, 0);

        int hour, minute;
        var past = Regex.Match(t, @"^(?<q>half|quarter)\s+past\s+(?<h>\d{1,2}|[a-z]+)");
        if (past.Success)
        {
            var h = past.Groups["h"].Value;
            hour = int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : Array.IndexOf(Numbers, h);
            minute = past.Groups["q"].Value == "half" ? 30 : 15;
        }
        else
        {
            var clock = Regex.Match(t, @"^(?<h>\d{1,2})(?::(?<m>\d{2}))?");
            if (!clock.Success) return null;
            hour = int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture);
            minute = clock.Groups["m"].Success ? int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        }
        if (hour is < 0 or > 23 || minute > 59) return null;
        if (hour > 12) return new TimeOnly(hour, minute);
        if (hour == 0) return new TimeOnly(0, minute);

        bool? afternoon =
            Regex.IsMatch(t, @"p\.?\s?m|afternoon|evening|tonight") ? true
            : Regex.IsMatch(t, @"a\.?\s?m|morning") ? false
            : Regex.IsMatch(t, "night") ? hour is >= 6 and < 12
            : pm;
        afternoon ??= hour is >= 1 and <= 5;
        var h24 = afternoon.Value ? (hour == 12 ? 12 : hour + 12) : (hour == 12 ? 0 : hour);
        // "12 in the morning" is midnight, but "12:30" alone is lunchtime.
        if (!afternoon.Value && hour == 12 && !Regex.IsMatch(t, @"a\.?\s?m|morning|night")) h24 = 12;
        return new TimeOnly(h24, minute);
    }

    /// <summary>The answer, from the items on the day asked about.</summary>
    public static string Answer(Question question, IReadOnlyList<PlannerOccurrenceView> events, DateOnly day, DateOnly today)
    {
        var timed = events
            .Where(e => e.Start is not null && e.Date == day && e.Status is not ("skipped" or "cancelled"))
            .OrderBy(e => e.Start)
            .ToList();
        var on = day == today ? "today" : PlannerQuestion.DayWords(day, today);
        return question switch
        {
            At at => AtAnswer(timed, at.Time, on),
            Between between => BetweenAnswer(timed, between.From, between.To, on),
            Count count => CountAnswer(timed, count, on),
            _ => throw new ArgumentOutOfRangeException(nameof(question)),
        };
    }

    private static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Span(PlannerOccurrenceView e) =>
        e.End is TimeOnly end && end != e.Start ? $"{Hm(e.Start!.Value)} to {Hm(end)}" : Hm(e.Start!.Value);

    private static string List(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    private static string AtAnswer(IReadOnlyList<PlannerOccurrenceView> day, TimeOnly t, string on)
    {
        var starting = day.Where(e => e.Start == t).ToList();
        var running = day.Where(e => e.Start < t && e.End is TimeOnly end && end > t).ToList();
        var ending = day.Where(e => e.End == t && e.Start < t).ToList();
        if (starting.Count > 0)
        {
            var text = $"At {Hm(t)} {on} you have {List([.. starting.Select(e => $"“{e.Title}” ({Span(e)})")])}.";
            if (ending.Count > 0)
                text += $" {List([.. ending.Select(e => $"“{e.Title}”")])} {(ending.Count == 1 ? "ends" : "end")} then.";
            return text;
        }
        if (running.Count > 0)
            return $"At {Hm(t)} {on} you're in {List([.. running.Select(e => $"“{e.Title}” ({Span(e)})")])}.";
        var after = day.FirstOrDefault(e => e.Start > t);
        return $"Nothing is planned at {Hm(t)} {on}." + (after is null ? string.Empty : $" Next is “{after.Title}” at {Hm(after.Start!.Value)}.");
    }

    private static string BetweenAnswer(IReadOnlyList<PlannerOccurrenceView> day, TimeOnly from, TimeOnly to, string on)
    {
        var found = day.Where(e => e.Start < to && (e.End is TimeOnly end && end > e.Start ? end : e.Start!.Value.AddMinutes(1)) > from).ToList();
        if (found.Count == 0) return $"Nothing is planned between {Hm(from)} and {Hm(to)} {on}.";
        return $"Between {Hm(from)} and {Hm(to)} {on} you have {List([.. found.Select(e => $"“{e.Title}” ({Span(e)})")])}.";
    }

    private static string CountAnswer(IReadOnlyList<PlannerOccurrenceView> day, Count count, string on)
    {
        var key = count.Word is null ? null : WordStem.Of(count.Word);
        var found = key is null
            ? day
            : [.. day.Where(e => Regex.Matches(e.Title, @"[\p{L}\p{N}']+").Any(w => WordStem.Of(w.Value) == key))];
        if (found.Count == 0) return $"You have no {count.Many} in your planner {on}.";
        var many = found.Count == 1 ? Singular(count.Many) : count.Many;
        return $"You have {found.Count} {many} {on}: {List([.. found.Select(e => $"“{e.Title}” at {Hm(e.Start!.Value)}")])}.";
    }

    /// <summary>"lunches" to "lunch", "walks" to "walk".</summary>
    private static string Singular(string many) =>
        Regex.IsMatch(many, "(?:ch|sh|ss|x)es$") ? many[..^2]
        : many.EndsWith('s') && !many.EndsWith("ss", StringComparison.Ordinal) ? many[..^1]
        : many;
}
