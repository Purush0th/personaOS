using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// The reverse of <see cref="CalendarRequestGuard"/>: asked to be reminded or nudged, the user wants
/// a reminder, not an event in the planner. Told "In 30 minutes, nudge me to move the laundry", the
/// fine-tuned qwen3:4b proposed a planner item in every run (training run 2026-11-07-qwen3). When
/// the message asks to be reminded at a time it names and never asks for the calendar, the item
/// becomes a reminder at that time; <see cref="UserTimeGuard"/>, after this one, holds it there.
/// </summary>
public sealed partial class ReminderNotEventGuard : IToolCallGuard
{
    private const string Reminder = "create_reminder";
    private const string PlannerItem = "add_planner_item";

    [GeneratedRegex(@"\b(?:calendar|planner|diary|agenda|schedule|book|pencil|plan)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForCalendar();

    /// <summary>"nudge me to move the laundry", "remind me about the dentist".</summary>
    [GeneratedRegex(
        @"\b(?:remind|nudge|ping|alert|notify|wake)\s+me\s+(?:to|about|that|of)\s+(?<what>.+?)\s*(?:$|[.!?]|,\s*(?:in|at|on|tomorrow|today|tonight)\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WhatToBeRemindedOf();

    public string Name => "reminder-not-event";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name != PlannerItem || !turn.ToolNames.Contains(Reminder) || turn.UserMessage is not { } message
            || turn.UserNow is not DateTime now
            || !ReminderRequestGuard.AsksForAReminder(message) || AsksForCalendar().IsMatch(message)
            || WhenPhrase.Read(message, now)?.Moment is not DateTime at)
            return NoVerdict;

        string? title;
        try
        {
            title = (JsonNode.Parse(call.InputJson) as JsonObject)?["title"] is JsonValue value
                    && value.GetValueKind() == JsonValueKind.String
                ? value.GetValue<string>()
                : null;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (string.IsNullOrWhiteSpace(title) && WhatToBeRemindedOf().Match(message) is { Success: true } said)
            title = WhenPhrase.WithoutTrailingWhen(said.Groups["what"].Value);
        if (string.IsNullOrWhiteSpace(title)) return NoVerdict;

        var input = new JsonObject
        {
            ["message"] = char.ToUpperInvariant(title[0]) + title[1..],
            ["dueAtLocal"] = at.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
            call with { Name = Reminder, InputJson = input.ToJsonString() }, "asked to be reminded, not for an event"));
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
