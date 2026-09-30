using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A reply that promises to look something up and ends there — "I'll list all the tasks in this
/// sprint… Hold tight while I do that", "Please wait, I'll run this for you" — or hands the lookup
/// back to the user — "I don't have the specific task details… you can find their value points on
/// the sprint board" — with no tool called in the turn. Either way the user gets no answer to a
/// question a tool could have answered. Seen on qwen2.5:3b (2026-09-27), four replies in a row.
/// The chat loop gives the model one round to make the call.
/// </summary>
public sealed partial class PromisedLookupGuard : IReplyGuard
{
    [GeneratedRegex(
        @"\b(?:I'?ll|I\s+will|let\s+me|I'?m\s+going\s+to|I\s+am\s+going\s+to)\s+(?:now\s+|quickly\s+|just\s+|first\s+)?(?:check|look|list|fetch|get|pull|review|see|confirm|find|verify|run|go\s+through|use)\b"
        + @"|\blet'?s\s+(?:now\s+|first\s+)?(?:check|look|review|see|get|pull|fetch|go\s+through)\b"
        + @"|\bI\s+need\s+to\s+(?:see|check|look\s+at|review|get)\b"
        + @"|\b(?:hold\s+tight|please\s+wait|one\s+moment|just\s+a\s+(?:moment|second)|give\s+me\s+a\s+(?:moment|second))\b"
        + @"|\bI\s+(?:don'?t|do\s+not)\s+have\s+(?:the\s+|any\s+|that\s+|this\s+)?(?:specific\s+|current\s+|exact\s+|detailed\s+)?(?:task\s+|goal\s+|sprint\s+)?(?:details?|information|info|data)\b"
        + @"|\b(?:give|provide|tell|share\s+with)\s+(?:me\s+)?(?:the\s+|a\s+)?(?:(?:task|sprint|goal)\s+)?(?:(?:titles?|names?)\s+or\s+)?(?:(?:task|sprint|goal)\s+)?keys?\b"
        + @"|\bI\s+can\s+(?:get|pull|fetch|check|look\s+up)\s+the\s+(?:sprint\s+report|board|plan|backlog|goals?)\b"
        + @"|\byou\s+can\s+(?:find|see|check|view|look\s+at)\b.*\b(?:board|backlog|planner|goals?\s+page|timeline)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Promise();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)])\s+\S", RegexOptions.Multiline)]
    private static partial Regex ListLine();

    public string Name => "promised-lookup";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        // Only a turn that looked nothing up: after a tool ran, "let me know…" style lines are fine.
        if (draft.IsCorrection || draft.Turn.Receipts.Count > 0 || draft.Turn.Proposals.Count > 0)
            return ValueTask.FromResult<string?>(null);

        draft.PromisedLookup = Find(draft.Text);
        return ValueTask.FromResult(draft.PromisedLookup is null ? null : $"promised a lookup it never made: \"{draft.PromisedLookup}\"");
    }

    /// <summary>
    /// The first sentence that promises a lookup or hands it back to the user, if any. A promise
    /// followed by a list is an introduction ("Let's review the points: - TASK-1 … 5 points"), not
    /// a promise left hanging.
    /// </summary>
    public static string? Find(string text)
    {
        var promise = ReplySentences.Split(text).FirstOrDefault(s => Promise().IsMatch(s));
        if (promise is null) return null;
        var after = text[(text.IndexOf(promise, StringComparison.Ordinal) + promise.Length)..];
        return ListLine().IsMatch(after) ? null : promise;
    }
}
