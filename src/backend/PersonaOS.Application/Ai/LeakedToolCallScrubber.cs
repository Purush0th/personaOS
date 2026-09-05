using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Removes tool calls that a model wrote as prose instead of emitting through the
/// provider's tool-call channel.
///
/// Small local models (llama3.1:8b, observed) sometimes print the call as JSON in the
/// reply text — often using the tool *definition* shape ("parameters") rather than the
/// call shape ("arguments"). Nothing executes, so the user is shown internals, and the
/// text is persisted into history where the next turn copies the pattern. Stripping it
/// keeps the leak out of both the transcript and the model's own context.
///
/// Deliberately conservative: a JSON object is only removed when its "name" matches a
/// tool that is actually registered for this instance, so ordinary JSON in a reply
/// (a user asking "show me an example config") survives untouched.
/// </summary>
public static class LeakedToolCallScrubber
{
    /// <summary>An empty fenced block left behind once the call inside it was removed.</summary>
    private static readonly Regex EmptyFence = new(@"```[a-zA-Z]*\s*```", RegexOptions.Compiled);

    /// <summary>Three or more newlines collapse to a paragraph break.</summary>
    private static readonly Regex ExcessBlankLines = new(@"(\r?\n){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="text"/> with any leaked calls to <paramref name="toolNames"/>
    /// removed. Returns it unchanged when there is nothing to strip.
    /// </summary>
    public static string Scrub(string text, IReadOnlyCollection<string> toolNames)
    {
        if (string.IsNullOrEmpty(text) || toolNames.Count == 0 || !text.Contains('{'))
        {
            return text;
        }

        var kept = new StringBuilder(text.Length);
        var removedAny = false;
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                kept.Append(text[i]);
                i++;
                continue;
            }

            var end = FindObjectEnd(text, i);
            if (end < 0)
            {
                // Unbalanced brace — treat the rest as ordinary text.
                kept.Append(text, i, text.Length - i);
                break;
            }

            var candidate = text[i..(end + 1)];
            if (IsLeakedCall(candidate, toolNames))
            {
                removedAny = true;
            }
            else
            {
                kept.Append(candidate);
            }

            i = end + 1;
        }

        if (!removedAny) return text;

        var cleaned = EmptyFence.Replace(kept.ToString(), string.Empty);
        cleaned = ExcessBlankLines.Replace(cleaned, "\n\n");
        return cleaned.Trim();
    }

    /// <summary>
    /// Index of the '}' closing the object that opens at <paramref name="start"/>, or -1.
    /// String-aware so braces inside quoted values do not confuse the depth count.
    /// </summary>
    private static int FindObjectEnd(string s, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }

        return -1;
    }

    private static bool IsLeakedCall(string candidate, IReadOnlyCollection<string> toolNames)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(candidate);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("name", out var name)) return false;
            if (name.ValueKind != JsonValueKind.String) return false;

            var called = name.GetString();
            if (called is null) return false;
            if (!toolNames.Contains(called, StringComparer.OrdinalIgnoreCase)) return false;

            // Accept the argument key each shape uses, plus a bare {"name": "..."}.
            return doc.RootElement.TryGetProperty("arguments", out _)
                || doc.RootElement.TryGetProperty("parameters", out _)
                || doc.RootElement.TryGetProperty("input", out _)
                || CountProperties(doc.RootElement) == 1;
        }
    }

    private static int CountProperties(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject()) count++;
        return count;
    }
}
