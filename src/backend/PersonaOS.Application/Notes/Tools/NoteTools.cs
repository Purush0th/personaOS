using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Notes.Tools;

/// <summary>Shared plumbing for the note tools.</summary>
public abstract class NoteToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Notes;

    public virtual bool IsOffered(InstanceConfig config) => config.IsEnabled(InstanceConfig.Modules.Notes);

    public virtual bool NeedsConfirmation(InstanceConfig config) => Mutates;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) => Task.FromResult<string?>(null);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    /// <summary>The note id, also when a model sends it as a string ("3") or with a hash ("#3").</summary>
    protected static int? GetId(JsonElement input)
    {
        if (!input.TryGetProperty("noteId", out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString()?.Trim().TrimStart('#'), out var parsed) ? parsed : null;
    }

    /// <summary>"content" so the chat's receipt shows the note's text.</summary>
    protected static object ForModel(NoteDto n) => new { noteId = n.Id, content = n.Text };
}

public class GetNotesTool(INoteService notes) : NoteToolBase
{
    public override string Name => "get_notes";
    public override bool Mutates => false;

    public override string Description =>
        "Reads the user's notes: things they jotted down to deal with later (\"what did I note down?\"). " +
        "Leave query empty to list them all, the latest first.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Words to look for, e.g. \"plumber\"." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var found = await notes.ListAsync(GetString(input, "query"), ct);
        return ToolJson.Serialize(new { notes = found.Take(50).Select(ForModel), total = found.Count });
    }
}

/// <summary>
/// Saved straight away, with a receipt rather than a card: a note is meant to be quick, nothing is
/// lost by it, and it is deleted with one tap (owner, 2026-10-10).
/// </summary>
public partial class CreateNoteTool(INoteService notes) : NoteToolBase
{
    /// <summary>
    /// The request around a note, which a small model copies along with it: "add a note to meal
    /// prep today" saved as is, or "note to meal prep today" (2 in 10 on qwen2.5:3b, 2026-10-11).
    /// "Note" alone counts only with what joins it to the rest ("note to", "note:"), so a note
    /// that starts "Notes from the meeting" keeps its first word.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"^\s*(?:(?:can|could|would)\s+you\s+)?(?:please\s+)?(?:(?:add|make|create|take|write|jot)\s+(?:down\s+)?(?:a\s+)?notes?\s*(?:down\s*)?(?:that|to|:|-|–)?|notes?\s*(?:down\s*)?(?:that|to|:|-|–))\s+",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex RequestWords();

    /// <summary>The note without the request around it, starting with a capital: "Meal prep today".</summary>
    public static string Clean(string text)
    {
        var note = RequestWords().Replace(text.Trim(), string.Empty, 1).TrimEnd('?', ' ');
        if (note.Length == 0) return text.Trim();
        return char.ToUpperInvariant(note[0]) + note[1..];
    }

    public override string Name => "create_note";

    public override bool NeedsConfirmation(InstanceConfig config) => false;

    public override string Description =>
        "Jots down a note for the user to deal with later, when they say \"note …\", \"note down …\" or " +
        "\"add a note …\". The text as they said it. Not for a task on the board, a planner event, a reminder " +
        "or a memory, which have their own tools.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "description": "What the user asked to note, in their own words, copied: \"add a note to meal prep today\" is \"Meal prep today\". Do not reword it or add to it." }
          },
          "required": ["text"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(GetString(input, "text")))
            throw new NoteValidationException("'text' is required: what to note down.");
        return Task.CompletedTask;
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var saved = await notes.CreateAsync(Clean(GetString(input, "text")!), ct);
        return ToolJson.Serialize(new { noted = ForModel(saved) });
    }
}

/// <summary>The way out of the notes: a task in the backlog. A change to the board, so it is a card first.</summary>
public class MoveNoteToBacklogTool(INoteService notes) : NoteToolBase
{
    public override string Name => "move_note_to_backlog";

    public override bool IsOffered(InstanceConfig config) =>
        base.IsOffered(config) && config.IsEnabled(InstanceConfig.Modules.Board);

    public override string Description =>
        "Turns one of the user's notes into a task in the board's backlog (its first line the title, the rest " +
        "the description) and removes the note. Find its noteId with get_notes first.";

    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "noteId": { "type": "integer", "description": "The note's id from get_notes." }
          },
          "required": ["noteId"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetId(input) ?? throw new NoteValidationException("'noteId' is required: find it with get_notes.");
        _ = await notes.GetAsync(id, ct) ?? throw new NoteValidationException($"There is no note {id}. Use an id from get_notes.");
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetId(input) is int id && await notes.GetAsync(id, ct) is { } note
            ? $"“{NoteService.TaskFrom(note.Text).Title}”"
            : null;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var task = await notes.MoveToBacklogAsync(GetId(input)!.Value, ct);
        return ToolJson.Serialize(new { created = new { key = task!.Key, title = task.Title, column = "backlog" } });
    }
}
