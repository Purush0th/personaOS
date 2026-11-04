using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Planner;

/// <summary>
/// Questions about the planner that have one right answer in the data: "what's next?" and "when is
/// my X?". Owner's chat b4c7pvc2 (2026-11-04): qwen2.5:3b said there was no "transitions" event
/// and no meditation tomorrow with "Transition (13:10)" and "Meditation, lights out (22:00)" in
/// the list it had just read out, and answered "what's my next schedule" in the morning with the
/// 21:00 item. Matching a word to a title and comparing times is work for code, so the answer is
/// worked out here and replaces the model's.
/// </summary>
public static partial class PlannerQuestion
{
    /// <summary>"What's next?", "what's my next schedule", "next on my calendar", "what's coming up".</summary>
    [GeneratedRegex(
        @"\bwhat(?:'s|’s|\s+is)?\s+(?:my\s+)?next\b|\b(?:my\s+)?next\s+(?:schedule|event|item|appointment|meeting|thing|activity|plan)\b|\bwhat(?:'s|’s|\s+is)?\s+(?:coming\s+)?up\s+next\b|\bwhat\s+do\s+i\s+have\s+next\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Next();

    /// <summary>Asks when something is, or whether it is planned: "when is", "what time is", "do I have any", "find me when".</summary>
    [GeneratedRegex(
        @"\bwhen\b|\bwhat\s+time\b|\bthe\s+time\s+(?:for|of)\b|\b(?:do|did)\s+i\s+have\b|\bhave\s+(?:i\s+)?(?:got\s+)?any\b|^\s*(?:any|is\s+there|are\s+there)\b|\bhave\s+i\s+(?:planned|scheduled)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksWhen();

    /// <summary>A request to change something, which is not a question about when.</summary>
    [GeneratedRegex(@"\b(?:add|schedule\s+(?:a|an|my)|book|create|move|reschedule|cancel|delete|remove|remind\s+me|set\s+(?:a|an|up)|put|change|mark)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForAChange();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}]+)?")]
    private static partial Regex Word();

    /// <summary>Words that say how the question is asked, not what it is about.</summary>
    private static readonly HashSet<string> NotTheTopic = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "actually", "after", "am", "an", "and", "any", "anything", "are", "at", "be", "by", "calendar", "can",
        "check", "could", "day", "did", "do", "does", "doing", "event", "events", "find", "for", "from", "get", "give", "go",
        "going", "got", "have", "hey", "i", "i'm", "in", "is", "it", "item", "items", "just", "know", "let", "look", "me",
        "my", "need", "of", "ok", "okay", "on", "or", "plan", "planned", "planner", "please", "schedule", "scheduled",
        "session", "sessions", "set", "should", "so", "tell", "that", "the", "there", "this", "time", "times", "to",
        "today", "tomorrow", "tonight", "up", "want", "was", "what", "what's", "when", "where", "which", "will", "with",
        "would", "yeah", "yes", "you", "your", "morning", "afternoon", "evening", "night", "week", "next", "start",
        "starts", "begin", "begins", "happen", "happening", "monday", "tuesday", "wednesday", "thursday", "friday",
        "saturday", "sunday", "thing", "things", "activity", "slot", "booked", "kept", "put", "see", "show", "list",
    };

    /// <summary>Whether the message asks what comes next.</summary>
    public static bool AsksNext(string? message) =>
        !string.IsNullOrWhiteSpace(message) && Next().IsMatch(message) && !AsksForAChange().IsMatch(message);

    /// <summary>
    /// The words a "when is my X?" question is about, or an empty list when the message is not such a
    /// question.
    /// </summary>
    public static IReadOnlyList<string> TopicOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || !AsksWhen().IsMatch(message) || AsksForAChange().IsMatch(message) || AsksNext(message))
            return [];
        return Word().Matches(message)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !NotTheTopic.Contains(w) && !w.All(char.IsDigit))
            .Distinct()
            .ToList();
    }

    /// <summary>Whether a title is about one of <paramref name="topic"/>'s words, allowing for plurals and a one-letter mishearing.</summary>
    public static bool Matches(string title, IReadOnlyList<string> topic)
    {
        var words = Word().Matches(title).Select(m => Stem(m.Value)).ToList();
        return topic.Select(Stem).Any(t => words.Any(w => w == t || (t.Length >= 6 && w.Length >= 6 && Distance(t, w) <= 1)));
    }

    /// <summary>"Transitions" and "transition", "Meditation's" and "meditation".</summary>
    private static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        if (w.EndsWith("'s", StringComparison.Ordinal)) w = w[..^2];
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        return w;
    }

    /// <summary>Edit distance, stopping early: only 0, 1 and "more" matter.</summary>
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1) return 2;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }

    /// <summary>"at 13:10", "at 13:10 to 13:15", "all day".</summary>
    public static string When(PlannerOccurrenceView e) =>
        e.Start is TimeOnly start
            ? e.End is TimeOnly end && end != start
                ? string.Create(CultureInfo.InvariantCulture, $"at {start:HH:mm} to {end:HH:mm}")
                : string.Create(CultureInfo.InvariantCulture, $"at {start:HH:mm}")
            : "all day";

    /// <summary>"today", "tomorrow", or "on Friday 7 November".</summary>
    public static string DayWords(DateOnly day, DateOnly today) =>
        day == today ? "today"
        : day == today.AddDays(1) ? "tomorrow"
        : "on " + day.ToString("dddd d MMMM", CultureInfo.InvariantCulture);

    /// <summary>The answer to "when is my X?" from the items found, or null when none was.</summary>
    public static string? WhenAnswer(string message, IReadOnlyList<PlannerOccurrenceView> found, DateOnly today)
    {
        if (found.Count == 0) return null;
        var yes = Regex.IsMatch(message, @"^\s*(?:(?:do|did)\s+i|have|has|any|is\s+there|are\s+there)\b", RegexOptions.IgnoreCase);
        var text = new StringBuilder(yes ? "Yes: " : string.Empty);
        var parts = found.Take(4).Select(e => $"“{e.Title}” {When(e)} {DayWords(e.Date, today)}").ToList();
        text.Append(parts.Count == 1 ? parts[0] : string.Join("; ", parts[..^1]) + " and " + parts[^1]);
        text.Append('.');
        var sentence = text.ToString();
        return yes ? sentence : char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>The answer to "what's next?" at <paramref name="now"/>, from today's and tomorrow's items.</summary>
    public static string NextAnswer(IReadOnlyList<PlannerOccurrenceView> today, IReadOnlyList<PlannerOccurrenceView> tomorrow, DateTime now)
    {
        var time = TimeOnly.FromDateTime(now);
        bool Open(PlannerOccurrenceView e) => e.Status is not ("done" or "skipped" or "cancelled");
        var timed = today.Where(e => e.Start is TimeOnly && Open(e)).OrderBy(e => e.Start).ToList();
        var current = timed.LastOrDefault(e => e.Start <= time && (e.End ?? e.Start) > time);
        var next = timed.FirstOrDefault(e => e.Start > time);

        if (next is not null)
        {
            var answer = $"Next is “{next.Title}” {When(next)}.";
            return current is null ? answer : $"Right now it's “{current.Title}”, until {current.End:HH:mm}. {answer}";
        }

        var first = tomorrow.Where(e => e.Start is TimeOnly && Open(e)).OrderBy(e => e.Start).FirstOrDefault();
        var rest = current is null ? "Nothing else is planned for today." : $"“{current.Title}” runs until {current.End:HH:mm}, and nothing else is planned for today.";
        return first is null ? rest : $"{rest} Tomorrow starts with “{first.Title}” {When(first)}.";
    }
}
