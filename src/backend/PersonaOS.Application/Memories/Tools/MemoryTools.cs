using System.Text.Json;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Memories.Tools;

/// <summary>Shared plumbing for the memory tools.</summary>
public abstract class MemoryToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Memory;

    /// <summary>With auto-save on, a memory is saved straight away and receipted; off, it waits on a card.</summary>
    public virtual bool NeedsConfirmation(InstanceConfig config) => Mutates && !config.MemoryAutoSave;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) => Task.FromResult<string?>(null);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    /// <summary>The memory id, also when a model sends it as a string ("3") or with a hash ("#3").</summary>
    protected static int? GetId(JsonElement input)
    {
        if (!input.TryGetProperty("memoryId", out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString()?.Trim().TrimStart('#'), out var parsed) ? parsed : null;
    }

    protected static object ForModel(MemoryDto m) => new { memoryId = m.Id, content = m.Content, category = m.Category };

    protected const string CategorySchema = """
        "category": { "type": "string", "enum": ["fact", "preference", "project", "decision"], "description": "fact about the user, preference, ongoing project context, or a decision they made" }
        """;
}

public class SearchMemoriesTool(IMemoryService memories) : MemoryToolBase
{
    public override string Name => "search_memories";
    public override bool Mutates => false;

    public override string Description =>
        "Searches what you remember about the user from earlier conversations (facts, preferences, project context, " +
        "decisions). Use it before saying you do not know something personal, and to find a memory's id before " +
        "updating or deleting it. Leave query empty to list them all.";

    public override string InputSchemaJson => $$"""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Words to look for, e.g. \"workout\"." },
            {{CategorySchema}}
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var found = await memories.ListAsync(GetString(input, "query"), GetString(input, "category"), ct);
        return ToolJson.Serialize(new { memories = found.Take(30).Select(ForModel), total = found.Count });
    }
}

public class CreateMemoryTool(IMemoryService memories, IChatContext chat) : MemoryToolBase
{
    public override string Name => "create_memory";

    public override string Description =>
        "Remembers one durable thing the user told you, so later conversations know it: a fact about them, a " +
        "preference, ongoing project context or a decision. Only what the user said, in one short sentence; " +
        "never a guess. Not for tasks, goals, planner items or reminders, which have their own tools.";

    public override string InputSchemaJson => $$"""
        {
          "type": "object",
          "properties": {
            "content": { "type": "string", "description": "The memory, one short sentence, e.g. \"Prefers morning workouts\"." },
            {{CategorySchema}}
          },
          "required": ["content"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(GetString(input, "content")))
            throw new MemoryValidationException("'content' is required: the memory, as one short sentence.");
        MemoryService.NormalizeCategory(GetString(input, "category"));
        return Task.CompletedTask;
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var saved = await memories.CreateAsync(GetString(input, "content")!, GetString(input, "category"), chat.ConversationId, ct);
        return ToolJson.Serialize(new { remembered = ForModel(saved) });
    }
}

public class UpdateMemoryTool(IMemoryService memories) : MemoryToolBase
{
    public override string Name => "update_memory";

    public override string Description =>
        "Changes a memory when the user corrects it or it is out of date. Find its memoryId with search_memories first.";

    public override string InputSchemaJson => $$"""
        {
          "type": "object",
          "properties": {
            "memoryId": { "type": "integer", "description": "The memory's id from search_memories." },
            "content": { "type": "string", "description": "The corrected memory, one short sentence." },
            {{CategorySchema}}
          },
          "required": ["memoryId"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetId(input) ?? throw new MemoryValidationException("'memoryId' is required: find it with search_memories.");
        _ = await memories.GetAsync(id, ct) ?? throw new MemoryNotFoundException(id);
        if (GetString(input, "content") is null && GetString(input, "category") is null)
            throw new MemoryValidationException("Say what to change: 'content', 'category' or both.");
        if (GetString(input, "category") is { } category) MemoryService.NormalizeCategory(category);
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetId(input) is int id && await memories.GetAsync(id, ct) is { } memory ? $"“{memory.Content}”" : null;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var saved = await memories.UpdateAsync(GetId(input)!.Value, GetString(input, "content"), GetString(input, "category"), ct);
        return ToolJson.Serialize(new { updated = ForModel(saved) });
    }
}

public class DeleteMemoryTool(IMemoryService memories) : MemoryToolBase
{
    public override string Name => "delete_memory";

    public override string Description =>
        "Forgets a memory when the user asks you to, or it is no longer true. Find its memoryId with search_memories first.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "memoryId": { "type": "integer", "description": "The memory's id from search_memories." }
          },
          "required": ["memoryId"]
        }
        """;

    /// <summary>Forgetting cannot be undone, so it always waits on a card, auto-save or not.</summary>
    public override bool NeedsConfirmation(InstanceConfig config) => true;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetId(input) ?? throw new MemoryValidationException("'memoryId' is required: find it with search_memories.");
        _ = await memories.GetAsync(id, ct) ?? throw new MemoryNotFoundException(id);
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetId(input) is int id && await memories.GetAsync(id, ct) is { } memory ? $"“{memory.Content}”" : null;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetId(input) ?? throw new MemoryValidationException("'memoryId' is required: find it with search_memories.");
        var memory = await memories.GetAsync(id, ct) ?? throw new MemoryNotFoundException(id);
        await memories.DeleteAsync(id, ct);
        return ToolJson.Serialize(new { forgot = new { content = memory.Content } });
    }
}
