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
public record ToolReceipt(string Tool, bool Ok, string? Summary)
{
    /// <summary>What ran, in the user's words ("Created goal"); worked out, so older stored receipts have it too.</summary>
    public string Label => ToolLabel.Done(Tool);
}

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
    /// <summary>Fields worth appending as context, in display order.</summary>
    private static readonly string[] DetailFields =
        ["key", "dueAtLocal", "date", "scheduledTime", "periodType", "periodStart", "status", "column"];

    public static ToolReceipt Build(string toolName, string resultJson, bool isError) =>
        new(toolName, !isError, isError ? Failure(resultJson) : Summarise(resultJson));

    /// <summary>
    /// Error results are <c>{"error":"…"}</c> from the registry. The user saw the raw JSON on the
    /// failed card ("✕ {"error":"Goal 5 does not exist."}"), so unwrap it to the sentence inside.
    /// </summary>
    private static string? Failure(string resultJson)
    {
        var text = resultJson.Trim();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } message)
            {
                text = message.Trim();
            }
        }
        catch (JsonException)
        {
            // Not JSON: it is already plain text.
        }

        return ToolPayloadText.Truncate(text, 160);
    }

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

            var label = ToolPayloadText.FirstValue(root, ToolPayloadText.LabelFields);
            var details = DetailFields
                .Select(f => ToolPayloadText.FirstValue(root, [f]))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (label is null && details.Count == 0) return null;

            var summary = label is null
                ? string.Join(" · ", details)
                : details.Count == 0 ? label : $"{label} — {string.Join(" · ", details)}";

            return ToolPayloadText.Truncate(summary, 160);
        }
    }

    /// <summary>
    /// Descends through single-property wrapper objects (<c>{"created": {…}}</c>) to the payload
    /// that actually describes what happened. Stops at anything that is not a lone object or
    /// array property, so <c>{"ok": true}</c> and multi-field results are untouched.
    /// </summary>
    private static JsonElement Unwrap(JsonElement element)
    {
        // Bounded: a couple of levels covers the wrappers in use without looping on odd input.
        for (var depth = 0; depth < 3; depth++)
        {
            if (element.ValueKind != JsonValueKind.Object) return element;
            if (ToolPayloadText.CountProperties(element) != 1) return element;

            var only = element.EnumerateObject().First().Value;
            if (only.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return element;

            element = only;
        }

        return element;
    }

}
