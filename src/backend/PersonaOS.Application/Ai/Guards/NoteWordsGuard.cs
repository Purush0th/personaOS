using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A note is saved without a card, so a note in other words than the user's is saved wrong with
/// nobody to catch it. Asked "Can you add a note to meal prep today?", qwen2.5:3b saved "Note to
/// self: Prepare dinner ingredients for tomorrow" (owner, 2026-10-11). So most of a note's words
/// must come from what the user wrote; otherwise the call is answered with the user's message, to
/// call again with their words.
/// </summary>
public sealed partial class NoteWordsGuard : IToolCallGuard
{
    private const string NoteTool = "create_note";

    /// <summary>Words that say nothing about what the note is.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "your", "can", "could", "would", "please", "add", "note", "notes", "noted",
        "down", "jot", "self", "that", "this", "with", "about", "from", "into", "then", "need", "needs", "remember",
        "make", "write", "put", "take", "also", "just", "some", "are", "was", "will", "have", "has",
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    public string Name => "note-words";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name != NoteTool || string.IsNullOrWhiteSpace(turn.UserMessage)) return NoVerdict;
        if (Text(call.InputJson) is not { } text) return NoVerdict;

        if (UsesTheUsersWords(text, turn.UserMessage))
        {
            // "Note meal prep today" for "add a note to meal prep today" (1 in 15, 2026-10-11): the
            // request's "note" came along. Dropped unless the user wrote it in front of that word.
            return WithoutLeadingNote(text, turn.UserMessage) is { } cleaned
                ? Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
                    call with { InputJson = JsonSerializer.Serialize(new { text = cleaned }) }, "dropped a leading \"note\""))
                : NoVerdict;
        }

        var message =
            $"This note is not in the user's words. They wrote: \"{turn.UserMessage.Trim()}\". "
            + $"Call {NoteTool} again with text taken from that, keeping their words (and any day they named).";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = message }), IsError: true, $"note not in the user's words: \"{text}\""));
    }

    /// <summary>
    /// Whether at least half of the note's telling words appear in the message, a word matching
    /// another that begins with it ("prep" and "prepare"). A note of filler alone passes.
    /// </summary>
    public static bool UsesTheUsersWords(string note, string message)
    {
        var said = Words(message).ToList();
        var noted = Words(note).ToList();
        if (noted.Count == 0) return true;
        var found = noted.Count(w => said.Any(s => SameWord(w, s)));
        return found * 2 >= noted.Count;
    }

    /// <summary>The note without a first word "note" the user did not write there, or null to keep it as it is.</summary>
    public static string? WithoutLeadingNote(string note, string message)
    {
        var match = LeadingNote().Match(note);
        if (!match.Success) return null;
        var next = match.Groups["next"].Value;
        if (Regex.IsMatch(message, $@"\bnote\s+{Regex.Escape(next)}\b", RegexOptions.IgnoreCase)) return null;
        var rest = note[match.Groups["rest"].Index..].Trim();
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    [GeneratedRegex(@"^\s*note\s+(?<rest>(?<next>[\p{L}\p{N}]+).*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LeadingNote();

    private static IEnumerable<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => (w.Length >= 3 || w.All(char.IsDigit)) && !Filler.Contains(w))
            .Distinct();

    private static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static string? Text(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("text", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
