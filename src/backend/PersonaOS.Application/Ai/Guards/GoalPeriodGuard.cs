using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Holds a new goal to the period the user named, and asks for one when they named none.
/// <list type="bullet">
/// <item>Asked "Set a goal: learn basic Spanish", qwen2.5:3b picked a month at random, sometimes one
/// already over, and put monthly goals under yearly ones (training capture, 2026-11-02). The owner
/// chose to ask rather than default (2026-11-03): a goal with no period in this message or the last
/// few is answered with "ask which period".</item>
/// <item>When the user named one ("this quarter", "next year", "in March", "for 2027"), the call is
/// rewritten to it, so the card shows the period they said.</item>
/// </list>
/// </summary>
public sealed partial class GoalPeriodGuard : IToolCallGuard
{
    private const string Goal = "create_goal";

    /// <summary>The refusal the user reads under a reply that still claims a goal was set.</summary>
    public const string NoPeriodGiven = "a goal needs a period, and none was given.";

    /// <summary>The question the reply becomes when the model did not ask one.</summary>
    public const string Ask = "Which period should this goal cover: this month, this quarter or this year?";

    public string Name => "goal-period";

    /// <summary>A period the user named.</summary>
    public sealed record Period(string Type, int Year, int? Quarter, int? Month);

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        // No message is no user words to hold the goal to (a call the server makes on its own).
        if (call.Name != Goal || !turn.MutatingTools.Contains(call.Name) || turn.UserMessage is null) return NoVerdict;

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

        var today = turn.UserToday ?? DateOnly.FromDateTime(DateTime.Today);
        var said = Read(turn.UserMessage, today)
            ?? turn.EarlierUserMessages.AsEnumerable().Reverse().Select(m => Read(m, today)).FirstOrDefault(p => p is not null);

        if (said is null)
        {
            // "Put it under GOAL-2": the parent fixes the period's kind, and the service checks the rest.
            if (input["parentKey"] is JsonValue && GoalKey().IsMatch(turn.UserMessage)) return NoVerdict;

            turn.Refusals.Add(NoPeriodGiven);
            const string ask = "The user has not said which period this goal is for. Do not guess one: ask them, in one "
                + "short question, whether it is for this month, this quarter or this year, and call create_goal once they say.";
            return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
                JsonSerializer.Serialize(new { error = ask }), IsError: true, "goal with no period given"));
        }

        var before = input.ToJsonString();
        input["periodType"] = said.Type;
        input["year"] = said.Year;
        input.Remove("quarter");
        input.Remove("month");
        if (said.Quarter is { } q) input["quarter"] = q;
        if (said.Month is { } m) input["month"] = m;
        if (said.Type != "year") input.Remove("periodStart");
        // A parent of another kind would be refused by the service; the user's period wins.
        if (input["parentKey"] is JsonValue && !GoalKey().IsMatch(turn.UserMessage)) input.Remove("parentKey");

        return input.ToJsonString() == before
            ? NoVerdict
            : Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
                call with { InputJson = input.ToJsonString() }, $"period set to what the user said ({Describe(said)})"));
    }

    /// <summary>The period a message names for a goal, or null when it names none.</summary>
    public static Period? Read(string? message, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var quarterNow = (today.Month - 1) / 3 + 1;

        var relative = Relative().Match(message);
        if (relative.Success)
        {
            // "next" and "the coming" move one on; "this" and "end of the" do not.
            var which = relative.Groups["which"].Value;
            var next = which.Length > 0 && !which.Equals("this", StringComparison.OrdinalIgnoreCase);
            switch (relative.Groups["unit"].Value.ToLowerInvariant())
            {
                case "year":
                    return new Period("year", today.Year + (next ? 1 : 0), null, null);
                case "quarter":
                    var q = quarterNow + (next ? 1 : 0);
                    return q > 4 ? new Period("quarter", today.Year + 1, 1, null) : new Period("quarter", today.Year, q, null);
                default:
                    var month = today.AddMonths(next ? 1 : 0);
                    return new Period("month", month.Year, null, month.Month);
            }
        }

        var quarter = NamedQuarter().Match(message);
        if (quarter.Success)
        {
            var q = int.Parse(quarter.Groups["q"].Value, CultureInfo.InvariantCulture);
            var year = YearIn(quarter.Groups["year"].Value) ?? (q < quarterNow ? today.Year + 1 : today.Year);
            return new Period("quarter", year, q, null);
        }

        var named = NamedMonth().Match(message);
        if (named.Success)
        {
            var month = DateTime.ParseExact(named.Groups["month"].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
            var year = YearIn(named.Groups["year"].Value) ?? (month < today.Month ? today.Year + 1 : today.Year);
            return new Period("month", year, null, month);
        }

        var yearOnly = NamedYear().Match(message);
        if (yearOnly.Success) return new Period("year", int.Parse(yearOnly.Groups["year"].Value, CultureInfo.InvariantCulture), null, null);

        if (Kind().Match(message) is { Success: true } kind)
        {
            return kind.Value.ToLowerInvariant() switch
            {
                "monthly" => new Period("month", today.Year, null, today.Month),
                "quarterly" => new Period("quarter", today.Year, quarterNow, null),
                _ => new Period("year", today.Year, null, null),
            };
        }

        return null;
    }

    private static int? YearIn(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    private static string Describe(Period p) => p.Type switch
    {
        "year" => $"{p.Year}",
        "quarter" => $"Q{p.Quarter} {p.Year}",
        _ => $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(p.Month ?? 1)} {p.Year}",
    };

    [GeneratedRegex(@"\b(?<which>this|next|the\s+coming)\s+(?<unit>year|quarter|month)\b|\b(?:by\s+the\s+)?end\s+of\s+the\s+(?<unit>year|quarter|month)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    [GeneratedRegex(@"\b(?:q(?<q>[1-4])|(?<q>[1-4])(?:st|nd|rd|th)\s+quarter)(?:\s+(?:of\s+)?(?<year>20\d\d))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamedQuarter();

    // Full month names after "in", "for", "by" or "during", so "Marco" and "may I" are not months.
    [GeneratedRegex(@"\b(?:in|for|by|during|this|next)\s+(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\b(?:\s+(?<year>20\d\d))?", RegexOptions.IgnoreCase)]
    private static partial Regex NamedMonth();

    [GeneratedRegex(@"\b(?:in|for|by|during)\s+(?<year>20\d\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamedYear();

    [GeneratedRegex(@"\b(?:monthly|quarterly|yearly|annual|annually)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Kind();

    [GeneratedRegex(@"\bGOAL-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex GoalKey();

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
