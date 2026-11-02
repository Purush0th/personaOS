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

    /// <summary>A task, a reminder or a goal is asked for in other words, and the model handles those.</summary>
    [GeneratedRegex(@"\b(?:task|to-?do|ticket|board|backlog|sprint|remind|reminder|alarm|goal)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherThing();

    [GeneratedRegex(@"(?:\s+(?:on|at|for|this|next|in|from|by))+$", RegexOptions.IgnoreCase)]
    private static partial Regex DanglingWords();

    [GeneratedRegex(@"^(?:a|an|the|my)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    /// <summary>The booking the message asks for, or null when it does not plainly name what and which day.</summary>
    public static Booking? Parse(string? message, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(message) || OtherThing().IsMatch(message)) return null;
        if (Books().Match(message) is not { Success: true } match) return null;
        if (WhenPhrase.Read(message, now) is not { Day: DateOnly day } said) return null;

        var what = match.Groups["what"].Value;
        foreach (var words in new[] { said.DayWords, said.TimeWords }.Where(w => w.Length > 0))
            what = Regex.Replace(what, Regex.Escape(words), " ", RegexOptions.IgnoreCase);
        what = Regex.Replace(what, @"\s+", " ").Trim();
        what = DanglingWords().Replace(what, string.Empty).Trim();
        what = Article().Replace(what, string.Empty).Trim();
        if (what.Length < 3) return null;

        return new Booking(char.ToUpperInvariant(what[0]) + what[1..], day, said.Time);
    }
}
