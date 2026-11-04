using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Takes out what the user did not ask to read. Owner's chat b4c7pvc2 (2026-11-04): after one
/// planner read, qwen2.5:3b pasted the whole 27-item day into nearly every reply, "who is the
/// developer of PersonaOS?" included, and wrote about the user in the third person ("The user is
/// looking for the developer of PersonaOS…"), its own notes leaking into the answer.
/// <list type="bullet">
/// <item>A sentence about "the user" is the model talking to itself, never to the person reading,
/// and is taken out.</item>
/// <item>A long list (six lines or more) under a message that asks for nothing listable is taken
/// out with the line that leads into it, when anything else is left to say.</item>
/// </list>
/// </summary>
public sealed partial class UnaskedListGuard : IReplyGuard
{
    private const int LongList = 6;

    [GeneratedRegex(@"^\s*(?:[-*•]\s+|\d{1,2}[.)]\s+)|\(\d{1,2}:\d{2}\)\s*$|^\s*\d{1,2}:\d{2}\b")]
    private static partial Regex ListLine();

    [GeneratedRegex(@"^\s*(?:the\s+user(?:'s)?\s+(?:is|was|wants|asked|asks|has|needs|would|seems|might|may|appears|said|says|mentioned)\b|based\s+on\s+the\s+user'?s?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex AboutTheUser();

    /// <summary>A message that asks for something a list answers: the plan, the day, the board, "what do I have".</summary>
    [GeneratedRegex(
        @"\b(?:plans?|planner|schedule|agenda|calendar|lists?|show|tasks?|events?|reminders?|goals?|sprints?|board|backlog|notes?|items?|today|tomorrow|tonight|week|day|morning|afternoon|evening|what\s+do\s+i\s+have|what'?s\s+on|look\s+like|all|everything|continue|more|rest|again)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksForAList();

    public string Name => "unasked-list";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var reasons = new List<string>();
        var text = draft.Text;

        if (ReplySentences.Without(text, s => AboutTheUser().IsMatch(s)) is { } withoutNarration
            && withoutNarration.Trim().Length > 0
            && withoutNarration.Trim() != text.Trim())
        {
            text = withoutNarration;
            reasons.Add("took out a sentence about \"the user\"");
        }

        if (draft.Turn.UserMessage is { } message && !AsksForAList().IsMatch(message) && WithoutLongLists(text) is { } shorter)
        {
            text = shorter;
            reasons.Add("took out a list nobody asked for");
        }

        if (reasons.Count == 0) return ValueTask.FromResult<string?>(null);
        draft.Rewrite(text);
        return ValueTask.FromResult<string?>(string.Join("; ", reasons));
    }

    /// <summary><paramref name="text"/> without its long lists and their lead-in lines, or null when it has none or nothing else.</summary>
    public static string? WithoutLongLists(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var drop = new bool[lines.Length];
        var found = false;
        for (var i = 0; i < lines.Length;)
        {
            var end = i;
            while (end < lines.Length && (ListLine().IsMatch(lines[end]) || (end > i && lines[end].Trim().Length == 0 && end + 1 < lines.Length && ListLine().IsMatch(lines[end + 1]))))
                end++;
            if (lines.Skip(i).Take(end - i).Count(l => l.Trim().Length > 0) >= LongList)
            {
                found = true;
                for (var j = i; j < end; j++) drop[j] = true;
                // "Here is your planner for tomorrow:" goes with its list.
                var lead = i - 1;
                while (lead >= 0 && lines[lead].Trim().Length == 0) lead--;
                if (lead >= 0 && lines[lead].TrimEnd().EndsWith(':')) drop[lead] = true;
            }
            i = Math.Max(end, i + 1);
        }
        if (!found) return null;

        var rest = string.Join("\n", lines.Where((_, i) => !drop[i]));
        rest = Regex.Replace(rest, @"\n{3,}", "\n\n").Trim();
        return rest.Length == 0 ? null : rest;
    }
}
