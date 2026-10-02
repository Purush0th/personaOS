using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Checks the day in a change against the day the user named. Asked to plan something "for today"
/// or to be reminded "at 7pm tomorrow", qwen2.5:3b wrote the day after in about one try in five,
/// with the dates listed in its prompt (accuracy suite, 2026-10-01). Prompt wording did not stop
/// it; this does: when the message names exactly one of yesterday, today or tomorrow and no other
/// date, a call for a different day is answered with the right date to call again with.
///
/// A message with two such words, a weekday, a month or an explicit date is left alone, since
/// which day each part means is not certain.
/// </summary>
public sealed partial class RelativeDateGuard : IToolCallGuard
{
    /// <summary>The fields that carry the day a change is for.</summary>
    private static readonly string[] DayFields = ["date", "dueAtLocal", "startsOn"];

    [GeneratedRegex(@"\b(?:today|tonight|this\s+(?:morning|afternoon|evening))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Today();

    [GeneratedRegex(@"\btomorrow\b", RegexOptions.IgnoreCase)]
    private static partial Regex Tomorrow();

    [GeneratedRegex(@"\byesterday\b", RegexOptions.IgnoreCase)]
    private static partial Regex Yesterday();

    /// <summary>Anything that names a day another way: a date, a weekday, a month, "next week".</summary>
    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}|\b\d{1,2}[/.]\d{1,2}\b|\b(?:mon|tue|wed|thu|fri|sat|sun)[a-z]*day\b|\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b|\bnext\s+(?:week|month)\b|\bin\s+\d+\s+days?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex OtherDay();

    public string Name => "relative-date";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.MutatingTools.Contains(call.Name) || turn.UserToday is not DateOnly today) return NoVerdict;
        if (IntendedDay(turn.UserMessage, today) is not (string word, DateOnly intended)) return NoVerdict;

        var given = DayIn(call.InputJson, call.Name, today);
        if (given is null || given == intended) return NoVerdict;

        var message =
            $"The user said \"{word}\", which is {Day(intended)}; this call is for {Day(given.Value)}. "
            + $"Call {call.Name} again with {intended:yyyy-MM-dd}.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = message }), IsError: true, $"day {given:yyyy-MM-dd} for \"{word}\""));
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);

    /// <summary>The one relative day the message names, or null when it names none, several, or another date.</summary>
    public static (string Word, DateOnly Day)? IntendedDay(string? message, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(message) || OtherDay().IsMatch(message)) return null;
        var named = new List<(string, DateOnly)>();
        if (Today().Match(message) is { Success: true } t) named.Add((t.Value.ToLowerInvariant(), today));
        if (Tomorrow().IsMatch(message)) named.Add(("tomorrow", today.AddDays(1)));
        if (Yesterday().IsMatch(message)) named.Add(("yesterday", today.AddDays(-1)));
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>
    /// The day a call is for, from its first day field; a planner item with no day is for today
    /// (the tool's default). Null when the call has no day or one that cannot be read.
    /// </summary>
    public static DateOnly? DayIn(string inputJson, string tool, DateOnly today)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var field in DayFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var text = value.GetString();
                if (text is null || text.Length < 10) return null;
                return DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day
                    : null;
            }
            return tool == "add_planner_item" ? today : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Day(DateOnly day) =>
        day.ToString("dddd yyyy-MM-dd", CultureInfo.InvariantCulture);
}
