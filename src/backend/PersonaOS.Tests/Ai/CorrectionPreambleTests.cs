using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The claim check is internal. When a model answers it conversationally, the lead-in must not
/// reach the user — it did on the phone, as "Sure, here is the corrected message:".
/// </summary>
public class CorrectionPreambleTests
{
    [Theory]
    // The reply seen on the phone, alpha.6.
    [InlineData("Sure, here is the corrected message: Would you like to delete the 'Create Tutorial' goal?",
        "Would you like to delete the 'Create Tutorial' goal?")]
    [InlineData("Here is the corrected message:\n\nWould you like me to set that reminder?",
        "Would you like me to set that reminder?")]
    [InlineData("Certainly! Here's the revised reply — Your goals are Learn Rust and Master AI.",
        "Your goals are Learn Rust and Master AI.")]
    [InlineData("Corrected message: Nothing has been saved yet.", "Nothing has been saved yet.")]
    [InlineData("Updated response:\nShall I create that goal?", "Shall I create that goal?")]
    public void Strips_the_lead_in(string reply, string expected)
    {
        Assert.Equal(expected, CorrectionPreamble.Strip(reply));
    }

    [Theory]
    // Ordinary replies that happen to use the same words.
    [InlineData("I corrected the message you asked about in your document.")]
    [InlineData("Here is the reminder you asked for: submit the report at 7pm.")]
    [InlineData("Sure. Your updated progress on Learn Rust is 40%.")]
    [InlineData("This is the version of the plan we agreed last week.")]
    [InlineData("")]
    public void Leaves_everything_else_alone(string reply)
    {
        Assert.Equal(reply, CorrectionPreamble.Strip(reply));
    }

    [Fact]
    public void A_reply_that_is_only_a_preamble_is_kept()
    {
        // Clumsy beats empty: an empty bubble tells the user nothing at all.
        const string reply = "Here is the corrected message:";
        Assert.Equal(reply, CorrectionPreamble.Strip(reply));
    }

    [Fact]
    public void Null_comes_back_as_an_empty_string()
    {
        Assert.Equal(string.Empty, CorrectionPreamble.Strip(null));
    }
}
