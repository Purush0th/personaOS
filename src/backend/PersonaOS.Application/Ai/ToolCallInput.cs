using System.Text.Json;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Unwraps tool arguments a model nested inside an envelope.
///
/// qwen3:4b sent <c>{"function":"add_planner_item","arguments":{"date":"2026-09-21","title":"Check
/// tasks"}}</c> where the tool expects <c>{"date":"2026-09-21","title":"Check tasks"}</c>. Every
/// field then reads as missing: the confirm card said only "Add planner item", and running it
/// would have failed with "'date' is required." Models produce this shape often enough that
/// meeting them halfway costs less than one more incident.
///
/// Applied where tool calls enter the chat loop, so the registry, the proposal card, the summary
/// and the repeat check all see the same bare arguments.
/// </summary>
public static class ToolCallInput
{
    /// <summary>Envelope keys whose value holds the real arguments.</summary>
    private static readonly string[] ArgumentFields = ["arguments", "parameters", "args", "input"];

    /// <summary>Keys an envelope carries alongside the arguments, never real tool fields.</summary>
    private static readonly string[] EnvelopeFields = ["function", "name", "tool", "tool_name", "type"];

    /// <summary>
    /// Returns the arguments object as JSON. Input that is already bare arguments comes back
    /// unchanged, as does anything unparseable — the tool reports its own error then.
    /// </summary>
    public static string Normalize(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return inputJson ?? string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            return inputJson;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return inputJson;

            foreach (var field in ArgumentFields)
            {
                if (!root.TryGetProperty(field, out var value)) continue;

                // Every other property must be part of the envelope. A tool that genuinely takes
                // an "input" field alongside its own keeps its arguments untouched.
                if (!OnlyEnvelopeAround(root, field)) continue;

                return value.ValueKind switch
                {
                    JsonValueKind.Object => value.GetRawText(),
                    // Some models send the arguments as a JSON string.
                    JsonValueKind.String => Normalize(value.GetString()),
                    _ => inputJson,
                };
            }

            return inputJson;
        }
    }

    private static bool OnlyEnvelopeAround(JsonElement root, string argumentField)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(argumentField)) continue;
            if (!EnvelopeFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
