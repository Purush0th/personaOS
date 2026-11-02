using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A memory is about something the user said. With auto-save on it is kept without a card, so a
/// made-up one is kept with nobody to catch it: told "Thanks, that's all for now", qwen2.5:3b saved
/// "The user appreciated the update" (accuracy suite, 2026-11-02). A memory that shares no telling
/// word with the user's messages, this one or the last few, is answered with "nothing to keep".
/// </summary>
public sealed partial class MemoryWordsGuard : IToolCallGuard
{
    private static readonly HashSet<string> MemoryTools = new(StringComparer.Ordinal) { "create_memory", "remember_about_user" };

    /// <summary>Words that say nothing about what is remembered.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "your", "user", "users", "they", "them", "their", "that", "this", "with", "about",
        "from", "into", "have", "has", "had", "was", "were", "are", "will", "would", "can", "could", "please",
        "remember", "keep", "mind", "note", "told", "said", "says", "likes", "like", "wants", "want", "prefers",
        "prefer", "conversation", "chat", "fact", "preference", "also", "just", "very", "really",
        // Courtesies: "Thanks, that's all for now" was kept as a memory that shared "thanks", "all"
        // and "now" with it, two tries in three (accuracy suite, 2026-11-02).
        "thanks", "thank", "thanked", "thanking", "appreciate", "appreciated", "all", "now", "okay", "cool",
        "great", "nice", "good", "bye", "goodbye", "cheers", "help", "helped", "helpful", "later",
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    public string Name => "memory-words";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!MemoryTools.Contains(call.Name) || string.IsNullOrWhiteSpace(turn.UserMessage) || Content(call.InputJson) is not { } content)
            return NoVerdict;

        var said = turn.EarlierUserMessages.Append(turn.UserMessage).SelectMany(Words).ToList();
        if (Words(content).Any(w => said.Any(s => SameWord(w, s)))) return NoVerdict;

        var message = $"\"{content}\" is not something the user told you. Save a memory only for a lasting fact or "
            + "preference the user stated; if they stated none, save nothing and just answer them.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = message }), IsError: true, $"memory not from the user: \"{content}\""));
    }

    private static IEnumerable<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => (w.Length >= 3 || w.All(char.IsDigit)) && !Filler.Contains(w));

    private static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static string? Content(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var field in new[] { "content", "message", "fact", "text" })
            {
                if (doc.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
