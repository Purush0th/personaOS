using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A task's title must come from what the user said. Asked only "Add a task to my backlog",
/// qwen2.5:3b proposed "Task for later" instead of asking what the task is (accuracy suite,
/// 2026-11-02). A title with nothing in it from the user's messages, this one or the last few, is
/// answered with "ask what the task is"; a title of filler words alone is never the user's.
/// A goal is held to the same rule: told "go ahead create items to the calendar", qwen2.5:3b
/// proposed a goal "Weekly Reflections" that no message mentioned (production chat, 2026-11-03).
/// </summary>
public sealed partial class TaskTitleGuard : IToolCallGuard
{
    private const string TaskTool = "create_task";
    private const string GoalTool = "create_goal";

    /// <summary>Words that say nothing about what the task is.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "your", "can", "could", "would", "please", "add", "new", "task", "tasks", "todo",
        "item", "items", "backlog", "board", "list", "later", "something", "thing", "stuff", "reminder", "create",
        "make", "put", "this", "that", "with", "about", "from", "into", "some", "untitled", "follow", "general",
        "pending", "misc", "work", "user", "unnamed", "placeholder",
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    public string Name => "task-title";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name is not (TaskTool or GoalTool) || string.IsNullOrWhiteSpace(turn.UserMessage)) return NoVerdict;
        var title = Title(call.InputJson);

        if (!string.IsNullOrWhiteSpace(title))
        {
            var said = turn.EarlierUserMessages.Append(turn.UserMessage).SelectMany(Words).ToList();
            var titled = Words(title).ToList();
            if (titled.Count > 0 && titled.Any(w => said.Any(s => SameWord(w, s)))) return NoVerdict;
        }

        // A missing or made-up title, and the message plainly says what the task is: that is the title.
        if (call.Name == TaskTool && Board.TaskRequest.TitleOf(turn.UserMessage) is { } asked && WithTitle(call.InputJson, asked) is { } input)
        {
            return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
                call with { InputJson = input }, $"title \"{title}\" to the user's \"{asked}\""));
        }

        if (string.IsNullOrWhiteSpace(title)) return NoVerdict; // the tool's own check says a title is needed

        var kind = call.Name == TaskTool ? "task" : "goal";
        turn.Refusals.Add($"the {kind} needs a title, and none was given.");
        var message = $"\"{title}\" is not something the user said. If they have not said what the {kind} is, ask them "
            + $"in one short question; otherwise call {call.Name} again with a title in their own words.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = message }), IsError: true, $"{kind} title not from the user: \"{title}\""));
    }

    private static IEnumerable<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => (w.Length >= 3 || w.All(char.IsDigit)) && !Filler.Contains(w));

    /// <summary>The same word, or one that begins with the other ("renew" and "renewal").</summary>
    private static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static string? WithTitle(string inputJson, string title)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson) is not System.Text.Json.Nodes.JsonObject input)
                return null;
            input["title"] = title;
            return input.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Title(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("title", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
