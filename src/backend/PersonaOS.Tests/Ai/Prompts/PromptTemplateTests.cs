using System.Globalization;
using PersonaOS.Application.Ai.Prompts;

namespace PersonaOS.Tests.Ai.Prompts;

public class PromptTemplateTests
{
    private static string Render(string body, params (string Name, object? Value)[] values) =>
        PromptTemplate.Parse("test", body).Render(values.ToDictionary(v => v.Name, v => v.Value));

    [Fact]
    public void Inserts_variables()
    {
        Assert.Equal("You are Juno.", Render("You are {{nickname}}.", ("nickname", "Juno")));
    }

    [Fact]
    public void Tolerates_spaces_inside_tags()
    {
        Assert.Equal("Hi Juno", Render("Hi {{ nickname }}", ("nickname", "Juno")));
    }

    [Fact]
    public void Formats_numbers_the_same_whatever_the_server_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.5", Render("{{n}}", ("n", 1.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Inserts_values_literally_so_user_text_cannot_inject_tags()
    {
        Assert.Equal("Persona: {{secret}}", Render("Persona: {{persona}}", ("persona", "{{secret}}")));
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "")]
    [InlineData("text", "on")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_section_renders_only_when_its_value_is_set(object? value, string expected)
    {
        Assert.Equal(expected, Render("{{#flag}}on{{/flag}}", ("flag", value)));
    }

    [Fact]
    public void An_inverted_section_renders_when_the_value_is_empty()
    {
        const string body = "{{#items}}some{{/items}}{{^items}}none{{/items}}";

        Assert.Equal("none", Render(body, ("items", Array.Empty<string>())));
        Assert.Equal("some", Render(body, ("items", new[] { "a" })));
    }

    [Fact]
    public void A_list_section_repeats_once_per_item()
    {
        Assert.Equal("[a][b]", Render("{{#items}}[{{.}}]{{/items}}", ("items", new[] { "a", "b" })));
    }

    [Fact]
    public void Outer_variables_stay_visible_inside_a_list()
    {
        Assert.Equal("x-a x-b ", Render("{{#items}}{{prefix}}-{{.}} {{/items}}",
            ("items", new[] { "a", "b" }), ("prefix", "x")));
    }

    [Fact]
    public void Section_tags_alone_on_a_line_take_the_line_with_them()
    {
        const string body = "Goals:\n{{#goals}}\n- {{.}}\n{{/goals}}\nEnd";

        Assert.Equal("Goals:\n- GOAL-1\n- GOAL-2\nEnd", Render(body, ("goals", new[] { "GOAL-1", "GOAL-2" })));
        Assert.Equal("Goals:\nEnd", Render(body, ("goals", Array.Empty<string>())));
    }

    [Fact]
    public void Inline_section_tags_keep_the_surrounding_text()
    {
        Assert.Equal("a b c", Render("a {{#x}}b{{/x}} c", ("x", true)));
    }

    [Fact]
    public void Comments_are_dropped_along_with_their_line()
    {
        Assert.Equal("one\ntwo", Render("one\n{{! why this rule exists }}\ntwo"));
    }

    [Fact]
    public void Front_matter_is_read_and_left_out_of_the_body()
    {
        var template = PromptTemplate.Parse("identity",
            "---\nname: identity\ndescription: \"Who the assistant is\"\ninputs:\n  nickname: the name\n---\nHello {{nickname}}");

        Assert.Equal("Who the assistant is", template.Description);
        Assert.Equal("Hello Juno", template.Render(new Dictionary<string, object?> { ["nickname"] = "Juno" }));
    }

    [Fact]
    public void Windows_line_endings_are_read_like_unix_ones()
    {
        Assert.Equal("a\nb", Render("---\r\nname: x\r\n---\r\na\r\nb"));
    }

    [Theory]
    [InlineData("{{missing}}", "unknown variable 'missing'")]
    [InlineData("{{#open}}never closed", "is never closed")]
    [InlineData("{{/stray}}", "not open")]
    [InlineData("{{#a}}{{/b}}", "not open")]
    [InlineData("{{bad name}}", "not a valid tag")]
    [InlineData("{{.}}", "only meaningful inside a section")]
    [InlineData("---\nname: x\nno closing line", "never closed")]
    public void A_broken_template_throws_instead_of_rendering_something_half_right(string body, string problem)
    {
        var error = Assert.Throws<PromptTemplateException>(() =>
            PromptTemplate.Parse("broken", body).Render(new Dictionary<string, object?> { ["open"] = true, ["a"] = true }));

        Assert.Contains(problem, error.Message);
        Assert.Equal("broken", error.Template);
    }
}
