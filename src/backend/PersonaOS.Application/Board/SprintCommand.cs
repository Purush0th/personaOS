using System.Text.RegularExpressions;

namespace PersonaOS.Application.Board;

/// <summary>
/// Reads a message that plainly asks to start a sprint: "Start the sprint", "Could you start
/// SPRINT-2", or "Just start it" right after talking about a sprint. Owner's chat b4c7pvc2
/// (2026-11-04): asked three times to start the planned sprint, qwen2.5:3b answered "The sprint
/// “Rebound” is now in progress" and never called <c>start_sprint</c>. The backend proposes the
/// start itself, like <see cref="TaskCommand"/>; the user still confirms the card.
/// </summary>
public static partial class SprintCommand
{
    /// <summary>"start the sprint", "begin my next sprint", "kick off SPRINT-2", "start the sprint that is in the backlog".</summary>
    [GeneratedRegex(
        @"\b(?:start|begin|kick\s+off|launch)\s+(?:(?:the|my|this|that|our|current|planned|next|new|upcoming)\s+){0,3}(?:sprint\b|SPRINT-\d+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex StartSprint();

    /// <summary>"start it", "just start it", "go ahead and start it", "start that one".</summary>
    [GeneratedRegex(@"\b(?:start|begin|kick\s+off)\s+(?:it|that(?:\s+one)?|this(?:\s+one)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StartIt();

    [GeneratedRegex(@"\bsprint\b|\bSPRINT-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex MentionsSprint();

    [GeneratedRegex(@"\bSPRINT-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex SprintKey();

    /// <summary>A question about starting, not a request to start: "How do I start a sprint?", "When should I start it?", "Did it start?".</summary>
    [GeneratedRegex(@"^\s*(?:how|when|why|what|where|did|does|is|has|should\s+i|do\s+i)\b|\b(?:don'?t|do\s+not|never|stop)\s+start", RegexOptions.IgnoreCase)]
    private static partial Regex NotARequest();

    /// <summary>The sprint the user asked to start, or null when they did not plainly ask.</summary>
    /// <param name="message">The user's message.</param>
    /// <param name="earlier">Their earlier messages, oldest first: "start it" counts only after talking about a sprint.</param>
    /// <returns>The request, with the sprint's key when they named one.</returns>
    public static StartRequest? Parse(string? message, IReadOnlyList<string> earlier)
    {
        if (string.IsNullOrWhiteSpace(message) || NotARequest().IsMatch(message)) return null;
        if (SprintKey().Matches(message).Select(m => m.Value.ToUpperInvariant()).Distinct().Count() > 1) return null;

        if (StartSprint().IsMatch(message)) return new StartRequest(KeyIn(message));

        // "Just start it": only when this message or one of the last few was about a sprint.
        var recent = earlier.TakeLast(3).Append(message).ToList();
        if (StartIt().IsMatch(message) && recent.Any(m => MentionsSprint().IsMatch(m)))
            return new StartRequest(Enumerable.Reverse(recent).Select(KeyIn).FirstOrDefault(k => k is not null));

        return null;
    }

    private static string? KeyIn(string text) =>
        SprintKey().Match(text) is { Success: true } m ? m.Value.ToUpperInvariant() : null;

    /// <summary>A request to start a sprint.</summary>
    /// <param name="SprintKey">The sprint named, or null for "the" sprint: the one planned.</param>
    public sealed record StartRequest(string? SprintKey);
}
