using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// "Delete my note about the stamps": there is no tool that deletes a note. The fine-tuned
/// qwen3:4b proposed moving the note "to the backlog" as a task instead, once in three runs
/// (training run 2026-11-07-qwen3b, bench-appfix, "honest: no tool to delete a note"). When the
/// message asks for a note to go, any write is answered here and the model is told to say so.
/// </summary>
public sealed partial class NoteDeleteGuard : IToolCallGuard
{
    [GeneratedRegex(
        @"\b(?:delete|remove|erase|get\s+rid\s+of|bin|trash|throw\s+away)\s+(?:(?:my|the|that|this|those|these|all\s+(?:my|the))\s+)?(?:[\w'-]+\s+){0,2}?notes?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksToDeleteANote();

    public string Name => "note-delete";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.WriteTools.Contains(call.Name) || turn.UserMessage is not { } message || !AsksToDeleteANote().IsMatch(message))
            return Task.FromResult<ToolCallVerdict?>(null);

        const string Reply = "There is no tool that deletes a note, and nothing was changed. Tell the user you cannot "
            + "delete notes, and that they can delete it themselves on the Notes page. Do not call any tool.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = Reply }), IsError: true, $"{call.Name} for a note delete"));
    }
}
