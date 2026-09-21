using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// A thinking model's reasoning is its private notes. Shown as the reply it confuses the user,
/// and stored in history it teaches the next turn to do the same.
/// </summary>
public class ThinkingBlockTests
{
    [Fact]
    public void Strips_a_reasoning_block_and_keeps_the_answer()
    {
        const string reply =
            "<think>The user asked for today. I should call get_planner first.</think>You have two "
            + "tasks today: file the tax return and read chapter 4.";

        Assert.Equal(
            "You have two tasks today: file the tax return and read chapter 4.",
            ThinkingBlock.Strip(reply));
    }

    [Theory]
    [InlineData("<thinking>hmm</thinking>Done.", "Done.")]
    [InlineData("<Think>\nmulti\nline\n</Think>\n\nDone.", "Done.")]
    [InlineData("<reasoning>why</reasoning> Done.", "Done.")]
    public void Handles_the_other_spellings(string reply, string expected)
    {
        Assert.Equal(expected, ThinkingBlock.Strip(reply));
    }

    [Fact]
    public void An_unclosed_block_takes_the_rest_with_it()
    {
        // A stream cut mid-thought has no answer in it either.
        Assert.Equal("Here goes.", ThinkingBlock.Strip("Here goes.<think>still thinking about"));
    }

    [Fact]
    public void A_reply_that_is_only_reasoning_comes_back_empty()
    {
        // The caller then says the model wrote no answer, rather than showing its notes.
        Assert.Equal(string.Empty, ThinkingBlock.Strip("<think>I should probably say hello</think>"));
    }

    [Theory]
    [InlineData("You have two tasks today.")]
    [InlineData("Use <b>bold</b> in the note.")]
    [InlineData("")]
    public void Leaves_ordinary_replies_alone(string reply)
    {
        Assert.Equal(reply, ThinkingBlock.Strip(reply));
    }
}
