using System.Text.Json;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Shared rendering of tool JSON into one readable line.
///
/// Two things describe a tool call to the user — the receipt of what a tool did
/// (<see cref="ToolReceiptBuilder"/>) and the card for what one is about to do
/// (<see cref="ProposedActionSummary"/>). They read the same payloads and must agree on how a
/// title, a timestamp or an overlong value is presented, so the reading lives here rather than
/// in both.
/// </summary>
internal static class ToolPayloadText
{
    /// <summary>Fields that name the thing being acted on, in order of preference.</summary>
    internal static readonly string[] LabelFields = ["title", "message", "task", "fileName", "name"];

    /// <summary>
    /// The first of <paramref name="names"/> present on <paramref name="element"/> as readable
    /// text, or null. Timestamps are tidied; other scalars are returned as written.
    /// </summary>
    internal static string? FirstValue(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text)) return Tidy(text);
        }

        return null;
    }

    /// <summary>ISO timestamps read badly on a one-line summary; drop the 'T' and the seconds.</summary>
    internal static string Tidy(string value)
    {
        if (value.Contains('T') && DateTime.TryParse(value, out var parsed))
        {
            return parsed.TimeOfDay == TimeSpan.Zero
                ? parsed.ToString("yyyy-MM-dd")
                : parsed.ToString("yyyy-MM-dd HH:mm");
        }

        return value;
    }

    internal static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    internal static int CountProperties(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject()) count++;
        return count;
    }
}
