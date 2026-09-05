using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// Covers stripping tool calls a model wrote as prose. The false-positive cases matter
/// as much as the stripping ones: the scrubber runs over every reply, so ordinary JSON
/// in an answer must survive untouched.
/// </summary>
public class LeakedToolCallScrubberTests
{
    private static readonly string[] Tools =
        ["read_document", "add_planner_item", "create_reminder", "get_goals"];

    [Fact]
    public void Strips_a_call_written_with_the_definition_shape()
    {
        // The exact shape llama3.1:8b produced: "parameters" rather than "arguments".
        const string text = """
            Unfortunately, I'm unable to find a function that directly answers your question. However, I can call the read_document function to retrieve the information on how to install the mobile app.

            {"name": "read_document", "parameters": {"fileName": "mobile_app_installation_guide.txt"}}
            """;

        var result = LeakedToolCallScrubber.Scrub(text, Tools);

        Assert.DoesNotContain("{", result);
        Assert.DoesNotContain("mobile_app_installation_guide", result);
        Assert.StartsWith("Unfortunately", result);
    }

    [Fact]
    public void Strips_a_call_written_with_the_call_shape()
    {
        const string text = """Sure. {"name": "add_planner_item", "arguments": {"title": "Buy milk"}}""";

        Assert.Equal("Sure.", LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Fact]
    public void Strips_a_call_inside_a_fenced_block_and_removes_the_empty_fence()
    {
        const string text = """
            Calling it now:

            ```json
            {"name": "create_reminder", "arguments": {"message": "Stand-up"}}
            ```
            """;

        var result = LeakedToolCallScrubber.Scrub(text, Tools);

        Assert.Equal("Calling it now:", result);
    }

    [Fact]
    public void Strips_every_call_when_the_model_emits_several()
    {
        const string text = """
            First {"name": "get_goals", "arguments": {}} then {"name": "read_document", "parameters": {"fileName": "a.txt"}} done.
            """;

        var result = LeakedToolCallScrubber.Scrub(text, Tools);

        Assert.DoesNotContain("get_goals", result);
        Assert.DoesNotContain("read_document", result);
        Assert.Contains("First", result);
        Assert.Contains("done.", result);
    }

    [Fact]
    public void Strips_a_bare_name_only_object()
    {
        const string text = """I will run {"name": "get_goals"} for you.""";

        var result = LeakedToolCallScrubber.Scrub(text, Tools);

        Assert.DoesNotContain("get_goals", result);
    }

    [Fact]
    public void Handles_braces_inside_string_values()
    {
        const string text = """{"name": "read_document", "parameters": {"fileName": "od{d}.txt"}} after""";

        Assert.Equal("after", LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Fact]
    public void Returns_empty_when_the_whole_reply_was_a_leaked_call()
    {
        const string text = """{"name": "get_goals", "arguments": {}}""";

        Assert.Equal(string.Empty, LeakedToolCallScrubber.Scrub(text, Tools));
    }

    // --- must NOT strip ------------------------------------------------------

    [Fact]
    public void Leaves_json_that_names_something_that_is_not_a_registered_tool()
    {
        const string text = """Here is a config: {"name": "my-service", "parameters": {"port": 8080}}""";

        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Fact]
    public void Leaves_json_with_no_name_property()
    {
        const string text = """Example: {"title": "Buy milk", "date": "2026-09-06"}""";

        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Fact]
    public void Leaves_prose_that_merely_mentions_a_tool_name()
    {
        const string text = "I used add_planner_item to save that for you.";

        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Fact]
    public void Leaves_text_alone_when_no_tools_are_registered()
    {
        const string text = """{"name": "read_document", "parameters": {}}""";

        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, []));
    }

    [Fact]
    public void Leaves_malformed_json_alone()
    {
        const string text = """Broken: {"name": "read_document", "parameters":""";

        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, Tools));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Just a normal reply with no JSON at all.")]
    public void Passes_through_text_with_nothing_to_strip(string text)
    {
        Assert.Equal(text, LeakedToolCallScrubber.Scrub(text, Tools));
    }
}
