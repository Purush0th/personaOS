using System.Text.Json;

namespace PersonaOS.Application.Ai;

/// <summary>
/// A record of one tool the app actually executed during a turn, summarised from the
/// tool's own result rather than from anything the model wrote.
/// </summary>
/// <param name="Tool">Tool name, e.g. "create_reminder".</param>
/// <param name="Ok">False when the tool reported an error.</param>
/// <param name="Summary">
/// Short human-readable outcome using the values that were actually stored, e.g.
/// "Travel to home — 2026-09-06 18:00". Null when the result has nothing worth showing
/// (a read that returned a list, say).
/// </param>
public record ToolReceipt(string Tool, bool Ok, string? Summary);

/// <summary>
/// Builds <see cref="ToolReceipt"/>s from raw tool results.
///
/// Exists because prompting cannot fix a model narrating a correct action incorrectly —
/// qwen2.5 stored a 6pm local reminder and then told the user it was 6pm UTC. The tool
/// result already carries the stored values, so surfacing them gives the user something
/// to check the prose against.
///
/// Provider-neutral by construction: it reads tool output, never model text.
/// </summary>
public static class ToolReceiptBuilder
{
    /// <summary>Fields that identify what was acted on, in order of preference.</summary>
    private static readonly string[] LabelFields = ["title", "message", "fileName", "name"];

    /// <summary>Fields worth appending as context, in display order.</summary>
    private static readonly string[] DetailFields =
        ["dueAtLocal", "date", "scheduledTime", "periodType", "periodStart", "status"];

    public static ToolReceipt Build(string toolName, string resultJson, bool isError) =>
        new(toolName, !isError, isError ? Failure(resultJson) : Summarise(resultJson));

    /// <summary>Error results are plain text from the registry; keep them short.</summary>
    private static string? Failure(string resultJson) => Truncate(resultJson.Trim(), 160);

    private static string? Summarise(string resultJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(resultJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            // Several tools wrap their payload — create_goal returns {"created": {…}} and
            // get_goals returns {"goals": […]}. Without unwrapping, the fields below are
            // invisible and the receipt comes back empty, which is exactly when the user
            // most needs it: a goal was created with the wrong period and no receipt said so.
            var root = Unwrap(doc.RootElement);

            // Tools return either the affected entity or a collection. A collection has no
            // single outcome to show, so report how many rows the model was given instead.
            if (root.ValueKind == JsonValueKind.Array)
            {
                var count = root.GetArrayLength();
                return count == 1 ? "1 item" : $"{count} items";
            }

            if (root.ValueKind != JsonValueKind.Object) return null;

            var label = FirstString(root, LabelFields);
            var details = DetailFields
                .Select(f => FirstString(root, [f]))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (label is null && details.Count == 0) return null;

            var summary = label is null
                ? string.Join(" · ", details)
                : details.Count == 0 ? label : $"{label} — {string.Join(" · ", details)}";

            return Truncate(summary, 160);
        }
    }

    /// <summary>
    /// Descends through single-property wrapper objects (<c>{"created": {…}}</c>) to the payload
    /// that actually describes what happened. Stops at anything that is not a lone object or
    /// array property, so <c>{"ok": true}</c> and multi-field results are untouched.
    /// </summary>
    private static int CountProperties(JsonElement element)
    {
        var count = 0;
        foreach (var _ in element.EnumerateObject()) count++;
        return count;
    }

    private static JsonElement Unwrap(JsonElement element)
    {
        // Bounded: a couple of levels covers the wrappers in use without looping on odd input.
        for (var depth = 0; depth < 3; depth++)
        {
            if (element.ValueKind != JsonValueKind.Object) return element;
            if (CountProperties(element) != 1) return element;

            var only = element.EnumerateObject().First().Value;
            if (only.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return element;

            element = only;
        }

        return element;
    }

    private static string? FirstString(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text)) return Tidy(text);
        }

        return null;
    }

    /// <summary>ISO timestamps read badly in a one-line receipt; drop the 'T' and seconds.</summary>
    private static string Tidy(string value)
    {
        if (DateTime.TryParse(value, out var parsed) && value.Contains('T'))
        {
            return parsed.TimeOfDay == TimeSpan.Zero
                ? parsed.ToString("yyyy-MM-dd")
                : parsed.ToString("yyyy-MM-dd HH:mm");
        }

        return value;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
