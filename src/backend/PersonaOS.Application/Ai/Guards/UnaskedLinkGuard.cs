using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Told "Haircut on Thursday at 11am - add it to my calendar", qwen2.5:3b linked the event to
/// TASK-1 and GOAL-2, and once to a TASK-1234 that does not exist, so the card was refused
/// (holdout3, 2026-11-02). A planner item is linked to a task or goal only when the user spoke of
/// one; otherwise the link is dropped before the card.
/// </summary>
public sealed partial class UnaskedLinkGuard : IToolCallGuard
{
    private const string PlannerItem = "add_planner_item";

    private static readonly string[] LinkFields = ["taskKey", "goalKey"];

    [GeneratedRegex(@"\b(?:TASK|GOAL)-\d+\b|\b(?:task|goal|sprint|board|backlog|to-?do)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpeaksOfWork();

    public string Name => "unasked-link";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name != PlannerItem || turn.UserMessage is not { } message) return NoVerdict;
        if (SpeaksOfWork().IsMatch(message) || turn.EarlierUserMessages.Any(m => SpeaksOfWork().IsMatch(m))) return NoVerdict;

        JsonObject? input;
        try
        {
            input = JsonNode.Parse(call.InputJson) as JsonObject;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (input is null) return NoVerdict;

        // An item with no title of its own takes the task's, so its link stays.
        if (input["title"] is not JsonValue title || !title.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return NoVerdict;

        var dropped = LinkFields
            .Where(f => input[f] is JsonValue v && v.TryGetValue<string>(out var key) && !string.IsNullOrWhiteSpace(key))
            .ToList();
        if (dropped.Count == 0) return NoVerdict;

        foreach (var field in dropped) input.Remove(field);
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
            call with { InputJson = input.ToJsonString() }, $"dropped {string.Join(", ", dropped)} nobody asked for"));
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
