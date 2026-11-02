using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders.Tools;

/// <summary>Shared plumbing for the reminder tools.</summary>
public abstract class ReminderToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Reminders;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing item override this to check it early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Tools that can fill in what the model left out override this.</summary>
    public virtual Task<string> CompleteInputAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult(input.GetRawText());

    /// <summary>Tools that act on an existing item name it, for the confirmation card.</summary>
    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    protected static bool GetBool(JsonElement input, string name, bool fallback = false) =>
        input.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    protected static string RequireString(JsonElement input, string name) =>
        GetString(input, name) ?? throw new ReminderValidationException($"'{name}' is required.");

    protected static int RequireInt(JsonElement input, string name) =>
        GetInt(input, name) ?? throw new ReminderValidationException($"'{name}' is required.");

    protected static string Ok(object payload) => ToolJson.Serialize(payload);
}

public class GetRemindersTool(IReminderService reminders) : ReminderToolBase
{
    public override string Name => "get_reminders";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the user's reminders with their due times. Pending only by default; " +
        "set includeCompleted to also see delivered, cancelled, and failed ones.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "includeCompleted": { "type": "boolean", "description": "Include non-pending reminders. Default false." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { reminders = await reminders.ListAsync(GetBool(input, "includeCompleted"), ct) });
}

/// <summary>
/// The links take only what the model was shown: a goal's key from get_goals, a planner item's id
/// from get_planner. At a low temperature qwen2.5:3b filled the optional "plannerItemId" with an
/// invented 123 on every try, so "remind me to call mum at 7pm tomorrow" never got a card
/// (2026-10-01, accuracy suite). Each link now says to leave it out unless the user asked for it.
/// </summary>
public class CreateReminderTool(IReminderService reminders, IGoalService goals) : ReminderToolBase
{
    public override string Name => "create_reminder";
    public override string Description =>
        "Schedules a reminder that notifies the user at a given time. 'dueAtLocal' is the " +
        "user's own wall-clock time (their time zone and today's date are in your context) — " +
        "resolve relative phrasing like 'tomorrow at 9am' into that value yourself. " +
        "Most reminders need only 'message' and 'dueAtLocal'.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "What to remind the user about." },
            "dueAtLocal": {
              "type": "string",
              "description": "Local date-time in the user's zone, ISO 8601 without offset, e.g. 2026-07-22T09:00:00."
            },
            "goalKey": {
              "type": "string",
              "description": "Only when the user ties the reminder to one of their goals: that goal's key from get_goals, e.g. \"GOAL-3\". Leave it out otherwise."
            },
            "plannerItemId": {
              "type": "integer",
              "description": "Only when the user ties the reminder to a planner item: the itemId get_planner returned for it. Leave it out otherwise; never guess an id."
            }
          },
          "required": ["message", "dueAtLocal"]
        }
        """;

    /// <summary>The goal the reminder is linked to, from its key ("GOAL-3"); null when none is given.</summary>
    private async Task<int?> GoalAsync(JsonElement input, CancellationToken ct)
    {
        var key = GetString(input, "goalKey");
        if (string.IsNullOrWhiteSpace(key)) return null;
        return await goals.ResolveKeyAsync(key, ct)
            ?? throw new ReminderValidationException(
                $"There is no goal {key}. Use a goalKey from get_goals (like \"GOAL-3\"), or leave it out.");
    }

    /// <summary>
    /// The time is the whole point of a reminder, so a card is never shown without one. A model
    /// proposed "Call mum at 7pm today" with no dueAtLocal at all: the card said when in prose
    /// and would have failed on confirm.
    /// </summary>
    /// <summary>
    /// Everything creating it checks, the time in the past included: qwen2.5:3b proposed a reminder
    /// for the day before, and the card only failed once confirmed (2026-10-01).
    /// </summary>
    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        await reminders.ValidateCreateAsync(new CreateReminderRequest(
            Message: RequireString(input, "message"),
            DueAtLocal: RequireDueAt(input),
            GoalId: await GoalAsync(input, ct),
            PlannerItemId: GetInt(input, "plannerItemId")), ct);

    private static DateTime RequireDueAt(JsonElement input)
    {
        var raw = RequireString(input, "dueAtLocal");
        return DateTime.TryParse(raw, out var local)
            ? local
            : throw new ReminderValidationException(
                "'dueAtLocal' must be an ISO date-time like 2026-07-22T09:00:00.");
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var local = RequireDueAt(input);

        return Ok(await reminders.CreateAsync(new CreateReminderRequest(
            Message: RequireString(input, "message"),
            DueAtLocal: local,
            GoalId: await GoalAsync(input, ct),
            PlannerItemId: GetInt(input, "plannerItemId")), ct));
    }
}

/// <summary>
/// Takes the reminder by its id or by its words. Asked to "cancel my reminder to call the bank",
/// qwen2.5:3b called it with reminderId 1, an id it had never been shown, three tries in three
/// (accuracy suite, 2026-11-02); the words it does know.
/// </summary>
public class CancelReminderTool(IReminderService reminders) : ReminderToolBase
{
    public override string Name => "cancel_reminder";
    public override string Description =>
        "Cancels a pending reminder so it will not fire. Name it by 'message' (a few of its words, e.g. " +
        "\"call the bank\"), or by the 'reminderId' get_reminders returned.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "A few words of the reminder to cancel, as the user named it." },
            "reminderId": { "type": "integer", "description": "Only an id get_reminders returned; never guess one." }
          }
        }
        """;

    /// <summary>A reminder named by its words gets its id: the one pending reminder they match.</summary>
    public override async Task<string> CompleteInputAsync(JsonElement input, CancellationToken ct = default)
    {
        if (GetString(input, "message") is not { Length: > 0 } words) return input.GetRawText();
        // An id that exists stands; a guessed one gives way to the words.
        if (GetInt(input, "reminderId") is int given && await reminders.GetAsync(given, ct) is { Status: ReminderStatuses.Pending })
            return input.GetRawText();

        var matches = ReminderMatch.Best(await reminders.ListAsync(includeCompleted: false, ct), words);
        return matches.Count == 1
            ? JsonSerializer.Serialize(new { reminderId = matches[0].Id })
            : input.GetRawText();
    }

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        if (GetInt(input, "reminderId") is not int id)
        {
            var words = GetString(input, "message");
            if (string.IsNullOrWhiteSpace(words)) throw new ReminderValidationException("Say which reminder: 'message' or 'reminderId'.");
            var matches = ReminderMatch.Best(await reminders.ListAsync(includeCompleted: false, ct), words);
            throw new ReminderValidationException(matches.Count == 0
                ? $"No pending reminder matches \"{words}\". Check get_reminders."
                : $"Several pending reminders match \"{words}\": "
                    + string.Join("; ", matches.Select(r => $"{r.Id} “{r.Message}” at {r.DueAtLocal:yyyy-MM-dd HH:mm}"))
                    + ". Ask the user which one.");
        }
        if (await reminders.GetAsync(id, ct) is null)
            throw new ReminderValidationException($"Reminder {id} does not exist. Use an id from get_reminders.");
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetInt(input, "reminderId") is int id && await reminders.GetAsync(id, ct) is { } reminder
            ? $"“{reminder.Message}” at {reminder.DueAtLocal:yyyy-MM-dd HH:mm}"
            : null;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "reminderId");
        var cancelled = await reminders.CancelAsync(id, ct);
        return cancelled is null
            ? throw new ReminderValidationException($"Reminder {id} does not exist.")
            : Ok(cancelled);
    }
}

/// <summary>Which pending reminders a few words name: those sharing the most telling words with them.</summary>
public static partial class ReminderMatch
{
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "my", "me", "to", "a", "an", "about", "reminder", "remind", "cancel", "delete", "remove", "that", "this",
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial System.Text.RegularExpressions.Regex Word();

    public static IReadOnlyList<ReminderDto> Best(IEnumerable<ReminderDto> pending, string words)
    {
        var wanted = Words(words).ToHashSet();
        if (wanted.Count == 0) return [];
        var scored = pending
            .Select(r => (Reminder: r, Score: Words(r.Message).Distinct().Count(w => wanted.Any(x => Same(w, x)))))
            .Where(s => s.Score > 0)
            .ToList();
        if (scored.Count == 0) return [];
        var top = scored.Max(s => s.Score);
        return [.. scored.Where(s => s.Score == top).Select(s => s.Reminder)];
    }

    private static IEnumerable<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3 && !Filler.Contains(w));

    private static bool Same(string a, string b)
    {
        if (a == b) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }
}
