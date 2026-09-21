using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// Models wrap tool arguments in an envelope. Unwrapping is what stands between the user and a
/// confirm card that says "Add planner item" with no item, and a tool that then fails on a field
/// the model did send.
/// </summary>
public class ToolCallInputTests
{
    [Fact]
    public void Unwraps_the_shape_qwen3_sent()
    {
        // Taken from the live database, 2026-09-21.
        const string wrapped =
            """{"function":"add_planner_item","arguments":{"date":"2026-09-21","title":"Check tasks"}}""";

        Assert.Equal(
            """{"date":"2026-09-21","title":"Check tasks"}""",
            ToolCallInput.Normalize(wrapped));
    }

    [Theory]
    [InlineData("""{"name":"create_goal","parameters":{"title":"Learn Rust"}}""")]
    [InlineData("""{"tool":"create_goal","args":{"title":"Learn Rust"}}""")]
    // Arguments sent as a JSON string rather than an object.
    [InlineData("""{"function":"create_goal","arguments":"{\"title\":\"Learn Rust\"}"}""")]
    public void Unwraps_the_other_envelopes(string wrapped)
    {
        Assert.Contains("\"title\"", ToolCallInput.Normalize(wrapped));
        Assert.DoesNotContain("create_goal", ToolCallInput.Normalize(wrapped));
    }

    [Theory]
    // Already bare.
    [InlineData("""{"date":"2026-09-21","title":"Check tasks"}""")]
    [InlineData("{}")]
    // A real field that happens to share an envelope name keeps its neighbours, so leave it.
    [InlineData("""{"input":"some text","date":"2026-09-21"}""")]
    // Not an object, not JSON, nothing at all: the tool reports its own error.
    [InlineData("""["one","two"]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Leaves_everything_else_untouched(string input)
    {
        Assert.Equal(input, ToolCallInput.Normalize(input));
    }

    [Fact]
    public void Null_comes_back_as_an_empty_string()
    {
        Assert.Equal(string.Empty, ToolCallInput.Normalize(null));
    }
}
