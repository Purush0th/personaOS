using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Asked not to forget something at a day and time, the user wants to be reminded, not a note. Told
/// "Don't let me forget to call my mother at 6pm tomorrow", the trained qwen2.5:3b saved a note in
/// three runs of three and said the reminder was set (training run 2026-11-06). When the message asks
/// to be reminded, names a day and a time and never says "note", the note becomes a reminder card;
/// <see cref="UserTimeGuard"/>, after this one, holds it to the day and time they said.
/// </summary>
public sealed partial class ReminderRequestGuard : IToolCallGuard
{
    private const string Note = "create_note";
    private const string Reminder = "create_reminder";

    [GeneratedRegex(
        @"\b(?:remind|reminder|alarm|ping|alert|notify|nudge|wake)\b|\b(?:don'?t|do\s+not|never)\s+let\s+me\s+forget\b|\bmake\s+sure\s+i\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksToBeReminded();

    [GeneratedRegex(@"\bnotes?\b|\bjot\b|\bwrite\s+(?:it|this|that)\s+down\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForANote();

    public string Name => "reminder-request";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name != Note || !turn.ToolNames.Contains(Reminder) || turn.UserMessage is not { } message
            || turn.UserNow is not DateTime now
            || !AsksToBeReminded().IsMatch(message) || AsksForANote().IsMatch(message)
            || WhenPhrase.Read(message, now)?.Moment is not DateTime at)
            return NoVerdict;

        string? text;
        try
        {
            text = (JsonNode.Parse(call.InputJson) as JsonObject)?["text"] is JsonValue value
                   && value.GetValueKind() == JsonValueKind.String
                ? value.GetValue<string>()
                : null;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (string.IsNullOrWhiteSpace(text)) return NoVerdict;

        var input = new JsonObject
        {
            ["message"] = text,
            ["dueAtLocal"] = at.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
            call with { Name = Reminder, InputJson = input.ToJsonString() }, "asked to be reminded, not a note"));
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
