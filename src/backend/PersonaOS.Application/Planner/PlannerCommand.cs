using System.Text.RegularExpressions;
using PersonaOS.Application.Ai.Guards;

namespace PersonaOS.Application.Planner;

/// <summary>
/// Reads a message that plainly books something on a day: "Book lunch with Marco tomorrow at 1pm",
/// "Schedule a call with Anna on Saturday at 11am". Told the first, qwen2.5:3b proposed a sprint
/// task twice and asked for a goal's title once (holdout2, 2026-11-02). The backend routes a request
/// this plain itself (docs/model-accuracy.md, FR-02); the user still confirms the card.
/// </summary>
public static partial class PlannerCommand
{
    /// <summary>The event to put on the calendar, read from the message.</summary>
    public sealed record Booking(string Title, DateOnly Date, TimeOnly? Time);

    [GeneratedRegex(
        @"^\s*(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:schedule|book|arrange|set\s+up|pencil\s+in)\s+(?:me\s+)?(?:in\s+)?(?<what>.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Books();

    /// <summary>
    /// "I have a dentist appointment tomorrow at 4pm, put it on my calendar": the event first, then
    /// the ask. Told this, qwen2.5:3b proposed a task once and a goal once in three (accuracy suite,
    /// 2026-11-02).
    /// </summary>
    [GeneratedRegex(
        @"^\s*(?<what>.+?)\s*[,.;:\-–—]+\s*(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:put|add|pop|stick)\s+(?:it|that|this)\s+(?:on|in|to|into)\s+(?:my|the)\s+(?:calendar|planner|diary|agenda)(?:\s+please)?[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex StatesThenAdds();

    /// <summary>
    /// "Add 'Buy milk' to my planner for today", "Put the eye test on my calendar on Monday at 5:45pm":
    /// the place named in the middle. The trained qwen2.5:3b called add_planner_item with no title for
    /// the first, then asked for the title it had been given (training run 2026-11-06).
    /// </summary>
    [GeneratedRegex(
        @"^\s*(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:add|put|pop|stick|get)\s+(?<what>.+?)\s+(?:on|in|to|into|onto)\s+(?:my|the)\s+(?:calendar|planner|diary|agenda)\b(?<rest>.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AddsToCalendar();

    [GeneratedRegex(@"^(?:i\s+have|i've\s+got|i\s+got|there's|there\s+is|i'm\s+having)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex IHave();

    /// <summary>A task, a reminder or a goal is asked for in other words, and the model handles those.</summary>
    [GeneratedRegex(@"\b(?:task|to-?do|ticket|board|backlog|sprint|remind|reminder|alarm|goal)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherThing();

    // "the" is left behind by "the day after tomorrow".
    [GeneratedRegex(@"(?:\s+(?:on|at|for|this|next|in|from|by|the))+$", RegexOptions.IgnoreCase)]
    private static partial Regex DanglingWords();

    [GeneratedRegex(@"^(?:a|an|the|my)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    /// <summary>The booking the message asks for, or null when it does not plainly name what and which day.</summary>
    public static Booking? Parse(string? message, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(message) || OtherThing().IsMatch(message)) return null;
        var match = Books().Match(message);
        if (!match.Success) match = StatesThenAdds().Match(message);
        if (!match.Success) match = AddsToCalendar().Match(message);
        if (!match.Success) return null;
        if (WhenPhrase.Read(message, now) is not { Day: DateOnly day } said) return null;

        var what = IHave().Replace(match.Groups["what"].Value.Trim().Trim('\'', '"', '“', '”', '‘', '’').Trim(), string.Empty);
        foreach (var words in new[] { said.DayWords, said.TimeWords }.Where(w => w.Length > 0))
            what = Regex.Replace(what, Regex.Escape(words), " ", RegexOptions.IgnoreCase);
        what = Regex.Replace(what, @"\s+", " ").Trim();
        what = DanglingWords().Replace(what, string.Empty).Trim();
        what = Article().Replace(what, string.Empty).Trim();
        if (what.Length < 3) return null;

        return new Booking(char.ToUpperInvariant(what[0]) + what[1..], day, said.Time);
    }
}
