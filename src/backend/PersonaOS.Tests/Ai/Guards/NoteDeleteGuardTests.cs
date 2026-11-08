using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

public class NoteDeleteGuardTests
{
    private static ChatTurnState Turn(string message) =>
        new("m", new HashSet<string> { "create_task" }, ["create_note", "create_task", "create_memory"],
            userMessage: message, writeTools: new HashSet<string> { "create_note", "create_task", "create_memory" });

    [Theory]
    [InlineData("Delete my note about the stamps")]
    [InlineData("Remove the shopping note")]
    [InlineData("get rid of that note")]
    [InlineData("Delete all my notes")]
    public async Task A_write_for_a_note_delete_is_answered_instead(string message)
    {
        // The fine-tuned qwen3:4b proposed moving the note to the backlog as a task (2026-11-07-qwen3b).
        var verdict = await new NoteDeleteGuard().CheckAsync(
            new AiToolCall("1", "create_task", """{"title":"Buy stamps for the wedding cards"}"""), Turn(message), default);

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
    }

    [Theory]
    [InlineData("Clear the gutters, put it in a note")]
    [InlineData("Note: remove the old shelf")]
    [InlineData("Make a note to delete old photos")]
    public async Task Other_writes_run(string message)
    {
        Assert.Null(await new NoteDeleteGuard().CheckAsync(
            new AiToolCall("1", "create_note", """{"text":"x"}"""), Turn(message), default));
    }
}
