using System.Text.RegularExpressions;

namespace PersonaOS.Application.Notes;

/// <summary>
/// "Is there a note about the wedding?", "Do I have any notes on the boiler?", "Did I note anything
/// about Sanjay?". One right answer, in the notes: the trained qwen2.5:3b said "None of the notes
/// mention a wedding" with "Buy stamps for the wedding cards" among them (training run 2026-11-06b,
/// bench-6). The notes that match are found here and the answer replaces the model's.
/// </summary>
public static partial class NoteQuestion
{
    [GeneratedRegex(
        @"^\s*(?:is\s+there|are\s+there|do\s+i\s+have|have\s+i\s+got|did\s+i\s+(?:write|jot|make|leave|save|take))\s+(?:a|an|any)?\s*(?:notes?|anything)\s*(?:down\s+)?(?:about|on|for|regarding|mentioning|that\s+mentions?)\s+(?<topic>.+?)[\s?.!]*$"
        + @"|^\s*did\s+i\s+note\s+(?:anything|something|down\s+anything)\s+(?:about|on|for|regarding)\s+(?<topic>.+?)[\s?.!]*$"
        + @"|^\s*what\s+(?:did\s+i\s+(?:note|write|jot)(?:\s+down)?|notes?\s+(?:do\s+i\s+have|did\s+i\s+(?:make|write|save|take)))\s+(?:about|on|for|regarding)\s+(?<topic>.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksAboutNotes();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    private static readonly HashSet<string> NotTheTopic = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "my", "a", "an", "our", "your", "his", "her", "their", "some", "any", "this", "that", "notes", "note",
    };

    /// <summary>The words the question is about ("wedding"), or an empty list when it is not such a question.</summary>
    public static IReadOnlyList<string> TopicOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || AsksAboutNotes().Match(message) is not { Success: true } m) return [];
        return Word().Matches(m.Groups["topic"].Value)
            .Select(w => w.Value.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !NotTheTopic.Contains(w))
            .Distinct()
            .ToList();
    }

    /// <summary>Whether a note is about every word of the topic, a plural or a possessive included.</summary>
    public static bool Matches(string text, IReadOnlyList<string> topic)
    {
        var words = Word().Matches(text).Select(w => Stem(w.Value)).ToHashSet();
        return topic.Count > 0 && topic.All(t => words.Contains(Stem(t)));
    }

    private static string Stem(string word)
    {
        var w = word.ToLowerInvariant();
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) w = w[..^1];
        return w;
    }

    /// <summary>The answer from the notes found: each quoted, or that none is about the topic.</summary>
    public static string Answer(IReadOnlyList<string> topic, IReadOnlyList<string> found)
    {
        var about = string.Join(" ", topic);
        if (found.Count == 0) return $"I couldn't find a note about “{about}”.";
        var quoted = found.Take(5).Select(t => $"“{t}”").ToList();
        var list = quoted.Count == 1 ? quoted[0] : string.Join(", ", quoted[..^1]) + " and " + quoted[^1];
        var more = found.Count > quoted.Count ? $", and {found.Count - quoted.Count} more" : string.Empty;
        return $"{(found.Count == 1 ? "One note" : $"{found.Count} notes")} about “{about}”: {list}{more}.";
    }
}
