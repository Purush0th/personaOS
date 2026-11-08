using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

public class CreativeAskGuardTests
{
    private static ChatTurnState Turn(string message) =>
        new("m", new HashSet<string> { "create_task" }, ["create_note", "create_task", "create_memory"],
            userMessage: message, writeTools: new HashSet<string> { "create_note", "create_task", "create_memory" });

    [Theory]
    [InlineData("Write a two-line poem about Monday mornings")]
    [InlineData("Suggest a name for a grey kitten")]
    [InlineData("Can you give me three ideas for a rainy Sunday?")]
    [InlineData("Make up a riddle about clocks")]
    public async Task A_note_for_something_to_write_is_answered_instead(string message)
    {
        // The fine-tuned qwen3:4b saved the poem request as a note in two runs of three (2026-11-07).
        var verdict = await new CreativeAskGuard().CheckAsync(
            new AiToolCall("1", "create_note", """{"text":"Write a two-line poem about Monday mornings"}"""), Turn(message), default);

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
    }

    [Theory]
    [InlineData("Write a note: gift ideas for Mum")]
    [InlineData("Suggest a name for the puppy and save it to my notes")]
    [InlineData("Put 'write the quarterly summary' on my board")]
    [InlineData("Write down that the boiler code was F28")]
    public async Task A_save_the_user_asked_for_runs(string message)
    {
        Assert.Null(await new CreativeAskGuard().CheckAsync(
            new AiToolCall("1", "create_note", """{"text":"x"}"""), Turn(message), default));
    }
}
