using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai.Intents;

/// <summary>What the user's latest message wants, in the app's terms.</summary>
public static class ChatIntents
{
    public const string CreateTask = "create_task";
    public const string CreateReminder = "create_reminder";
    public const string AddPlannerItem = "add_planner_item";
    public const string CreateGoal = "create_goal";
    public const string CreateNote = "create_note";
    public const string CreateMemory = "create_memory";
    public const string ChangeItem = "change_item";
    public const string Question = "question";
    public const string Conversation = "conversation";
    public const string Ambiguous = "ambiguous";

    /// <summary>
    /// The router: which tool carries out each intent that makes something. An explicit table, so
    /// the backend, not the model's prose, decides which workflow an intent may reach.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CreatingTool = new Dictionary<string, string>
    {
        [CreateTask] = "create_task",
        [CreateReminder] = "create_reminder",
        [AddPlannerItem] = "add_planner_item",
        [CreateGoal] = "create_goal",
        [CreateNote] = "create_note",
        [CreateMemory] = "create_memory",
    };

    /// <summary>
    /// One example per intent for the classifier. Without them qwen2.5:3b never answered
    /// "ambiguous": "I need to remember this" came back create_reminder three times in three, and a
    /// reminder with a time nobody gave was proposed (suite, 2026-10-12).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Example = new Dictionary<string, string>
    {
        [CreateTask] = "add 'fix the bike' to my backlog",
        [CreateReminder] = "remind me to water the plants at 6 pm",
        [AddPlannerItem] = "put lunch with Sam on Friday at 1 on my calendar",
        [CreateGoal] = "I want a goal to run a half marathon this year",
        [CreateNote] = "jot down: buy stamps",
        [CreateMemory] = "I'm vegetarian, keep that in mind",
        [ChangeItem] = "mark TASK-4 as done",
        // Names tasks on purpose: "What are my tasks for today?" came back create_task.
        [Question] = "what tasks do I have this week?",
        [Conversation] = "thanks, that's all",
        [Ambiguous] = "save this for me",
    };

    /// <summary>What each intent means, for the classifier and for the hint the chat model gets.</summary>
    public static readonly IReadOnlyDictionary<string, string> Meaning = new Dictionary<string, string>
    {
        [CreateTask] = "wants something to do added to their task board or backlog",
        [CreateReminder] = "wants to be reminded of something at a time",
        [AddPlannerItem] = "wants an event, appointment or plan put on their calendar for a day",
        [CreateGoal] = "wants a new goal",
        [CreateNote] = "wants a quick note jotted down to deal with later",
        [CreateMemory] = "tells you a lasting fact or preference about themselves to keep for future chats",
        [ChangeItem] = "wants an existing task, goal, reminder, event or note changed, moved, completed or deleted",
        [Question] = "asks a question, about their own data or anything else",
        [Conversation] = "greets, thanks, chats or thinks aloud without asking for any change",
        [Ambiguous] = "wants something kept, but does not say whether as a task, a reminder, an event or a note",
    };
}

/// <summary>An intent, and the tool the router allows for it when it makes something.</summary>
public sealed record IntentResult(string Intent, string? Tool)
{
    public bool IsChange => Tool is not null || Intent == ChatIntents.ChangeItem;
}

/// <summary>
/// Decides what the user's latest message wants before the chat model answers it
/// (docs/model-accuracy.md, owner 2026-10-12). The chat model chose by calling a tool or not, and a
/// small one often wrote "I have scheduled a reminder" instead of calling create_reminder. A
/// separate, short call with one job does better: it sees only the last few messages, and its
/// answer is held to a JSON schema of the intents on offer, so it cannot ramble or invent one.
/// Only intents whose tool is offered this turn are on offer. Anything unusable comes back null,
/// and the chat goes on as it did without a classification.
/// </summary>
public sealed class IntentClassifier(ILogger<IntentClassifier> logger)
{
    /// <summary>Earlier messages the classifier sees, for "yes" or "tomorrow 9 a.m." that continue one.</summary>
    private const int ContextMessages = 4;

    /// <summary>Each earlier message is cut to this, so a long reply does not crowd the latest one out.</summary>
    private const int MaxContextChars = 300;

    public async Task<IntentResult?> ClassifyAsync(
        IAiMessageStreamer streamer, AiRequest model, IReadOnlyList<AiChatTurn> history, string userMessage,
        IReadOnlyCollection<string> offeredTools, CancellationToken ct)
    {
        var intents = OnOffer(offeredTools);
        if (!intents.Any(i => ChatIntents.CreatingTool.ContainsKey(i))) return null;

        var request = model with
        {
            SystemPrompt = SystemPrompt(intents),
            Turns = [new AiChatTurn(ChatRoles.User, Transcript(history, userMessage))],
            Tools = [],
            // Same answer every time for the same message.
            Options = model.ModelOptions with { Temperature = 0 },
            ResponseSchemaJson = Schema(intents),
        };

        var text = new StringBuilder();
        try
        {
            await foreach (var chunk in streamer.StreamAsync(request, ct))
            {
                text.Append(chunk.TextDelta);
            }
        }
        catch (AiStreamException ex)
        {
            logger.LogWarning("Could not classify the message's intent; answering without: {Error}", ex.Message);
            return null;
        }

        var intent = Read(text.ToString());
        if (intent is null || !intents.Contains(intent))
        {
            logger.LogWarning("The intent check answered something unusable; answering without it.");
            return null;
        }

        logger.LogInformation("Intent of the message: {Intent}", intent);
        return new IntentResult(intent, ChatIntents.CreatingTool.GetValueOrDefault(intent));
    }

    /// <summary>The intents this turn can serve: those whose tool is offered, and those that make nothing.</summary>
    public static IReadOnlyList<string> OnOffer(IReadOnlyCollection<string> offeredTools) =>
        ChatIntents.Meaning.Keys
            .Where(i => ChatIntents.CreatingTool.TryGetValue(i, out var tool) ? offeredTools.Contains(tool) : true)
            .ToList();

    /// <summary>The intent in the classifier's reply, or null when it is not the JSON asked for.</summary>
    public static string? Read(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply.Trim());
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("intent", out var intent)
                && intent.ValueKind == JsonValueKind.String
                ? intent.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SystemPrompt(IReadOnlyList<string> intents)
    {
        var text = new StringBuilder(
            "You sort the user's latest message in a personal assistant app into one intent. Reply only with JSON "
            + "like {\"intent\": \"question\"}.\n\nIntents:\n");
        foreach (var intent in intents)
        {
            text.Append("- ").Append(intent).Append(": the user ").Append(ChatIntents.Meaning[intent])
                .Append(". Example: \"").Append(ChatIntents.Example[intent]).Append("\"\n");
        }
        text.Append("\nEarlier messages are there for context; classify only the latest. A short answer such as "
            + "\"yes\" or \"tomorrow 9 a.m.\" carries on with what was being discussed. When the user wants "
            + "something kept but does not say how (no time for a reminder, no day for an event, no board or "
            + "note named), the intent is ambiguous.");
        return text.ToString();
    }

    private static string Transcript(IReadOnlyList<AiChatTurn> history, string userMessage)
    {
        var earlier = history
            .Where(t => t.ToolCalls is null && t.ToolResults is null && t.Content.Length > 0)
            .SkipLast(1) // The history ends with the latest message, given on its own below.
            .TakeLast(ContextMessages)
            .ToList();

        var text = new StringBuilder();
        if (earlier.Count > 0)
        {
            text.Append("Earlier messages:\n");
            foreach (var turn in earlier)
            {
                var content = turn.Content.Length > MaxContextChars ? turn.Content[..MaxContextChars] + "…" : turn.Content;
                text.Append(turn.Role == ChatRoles.User ? "User: " : "Assistant: ").Append(content.ReplaceLineEndings(" ")).Append('\n');
            }
            text.Append('\n');
        }
        return text.Append("Latest message: ").Append(userMessage.Trim()).ToString();
    }

    private static readonly string[] Required = ["intent"];

    private static string Schema(IReadOnlyList<string> intents) =>
        JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new { intent = new { type = "string", @enum = intents } },
            required = Required,
            additionalProperties = false,
        });
}
