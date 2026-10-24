using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>
/// A note is saved without a card, so one in other words than the user's is sent back before it
/// is saved. Owner, 2026-10-11: "Can you add a note to meal prep today?" was saved as "Note to
/// self: Prepare dinner ingredients for tomorrow".
/// </summary>
public class NoteWordsGuardTests
{
    private static Task<ToolCallVerdict?> Check(string message, string text) =>
        new NoteWordsGuard().CheckAsync(
            new AiToolCall("1", "create_note", $$"""{"text":"{{text}}"}"""),
            new ChatTurnState("test-model", new HashSet<string>(), ["create_note"], userMessage: message),
            default);

    [Fact]
    public async Task A_note_in_other_words_is_sent_back_with_what_the_user_wrote()
    {
        var verdict = await Check("Can you add a note to meal prep today?", "Note to self: Prepare dinner ingredients for tomorrow");

        var answer = Assert.IsType<ToolCallVerdict.Answer>(verdict);
        Assert.True(answer.IsError);
        Assert.Contains("meal prep today", answer.Content);
    }

    [Theory]
    [InlineData("Can you add a note to meal prep today?", "Meal prep today")]
    [InlineData("Note down: call the plumber about the balcony leak", "Call the plumber about the balcony leak")]
    [InlineData("note buy milk", "Buy milk")]
    [InlineData("Add a note: prep the presentation", "Prepare the presentation")]
    [InlineData("Notiz: Ärztin anrufen", "Ärztin anrufen")]
    public async Task A_note_in_the_users_words_goes_through(string message, string text)
    {
        Assert.Null(await Check(message, text));
    }

    [Fact]
    public async Task A_note_led_by_the_requests_note_loses_it()
    {
        var verdict = await Check("Can you add a note to meal prep today?", "Note meal prep today");

        var rewrite = Assert.IsType<ToolCallVerdict.Rewrite>(verdict);
        Assert.Equal("""{"text":"Meal prep today"}""", rewrite.Call.InputJson);
    }

    [Theory]
    [InlineData("Note the meter reading", "Note the meter reading")]
    [InlineData("Add a note: note the meter reading", "Note the meter reading")]
    public async Task A_note_the_user_began_with_note_keeps_it(string message, string text)
    {
        Assert.Null(await Check(message, text));
    }

    [Fact]
    public async Task Other_tools_are_left_alone()
    {
        var verdict = await new NoteWordsGuard().CheckAsync(
            new AiToolCall("1", "create_memory", """{"content":"Something else entirely"}"""),
            new ChatTurnState("test-model", new HashSet<string>(), ["create_memory"], userMessage: "remember I like tea"),
            default);

        Assert.Null(verdict);
    }
}
