using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai.Prompts;

public class PromptLibraryTests
{
    private const string DefaultClock =
        "The user's time zone is Asia/Kolkata, where today is 2026-09-23 and the time is 20:40. Tomorrow is 2026-09-24. "
        + "Use these dates for \"today\", \"tomorrow\" and weekdays; never work a date out yourself:\n"
        + "- Wednesday 2026-09-23 (today)\n- Thursday 2026-09-24 (tomorrow)";

    private static readonly Dictionary<string, object?> Clock = new()
    {
        ["timeZone"] = "Asia/Kolkata",
        ["today"] = "2026-09-23",
        ["now"] = "20:40",
        ["tomorrow"] = "2026-09-24",
        ["week"] = new List<string> { "Wednesday 2026-09-23 (today)", "Thursday 2026-09-24 (tomorrow)" },
    };

    private readonly FakePromptOverrides _overrides = new();
    private readonly ListLogger<PromptLibrary> _logger = new();

    private PromptLibrary Library() => new(_overrides, _logger);

    [Fact]
    public void Ships_every_fragment_the_app_uses()
    {
        Assert.Equal(
            [
                "board", "board.compact", "brief-phrasing", "clock", "conversation-summary", "goals", "identity", "memory", "mode", "modules", "planner",
                "product", "product.compact", "reminders", "summarize", "summarize-input", "tool-rules", "tool-rules.compact",
            ],
            PromptLibrary.DefaultNames());
    }

    [Fact]
    public void Every_shipped_fragment_parses_and_says_what_it_is_for()
    {
        // Whoever edits an override starts from these, so each one explains itself.
        foreach (var name in PromptLibrary.DefaultNames())
        {
            Assert.False(string.IsNullOrWhiteSpace(PromptLibrary.Default(name).Description), $"{name} has no description");
        }
    }

    [Fact]
    public void Every_shipped_fragment_follows_the_prompty_format()
    {
        // The front matter follows the Prompty spec, so Prompty tooling (the VS Code extension)
        // opens and previews the files: the template names its engine, and every value the body
        // uses is declared under inputs with its kind, and nothing more.
        foreach (var name in PromptLibrary.DefaultNames())
        {
            var text = ShippedText(name);
            var close = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            var front = text[4..close];
            var body = text[(close + 4)..];

            Assert.Contains("\ntemplate:\n  format:\n    kind: mustache\n  parser:\n    kind: prompty", front);
            var declared = Regex.Matches(front, @"^  ([A-Za-z]\w*):\n    kind: (string|array|boolean)$", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
            var used = Regex.Matches(body, @"\{\{\s*[#^/]?\s*([A-Za-z]\w*)\s*\}\}")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
            Assert.True(declared.SetEquals(used),
                $"{name}: declared [{string.Join(", ", declared.Order())}] but uses [{string.Join(", ", used.Order())}]");
        }
    }

    private static string ShippedText(string name)
    {
        using var stream = typeof(PromptLibrary).Assembly.GetManifestResourceStream($"PersonaOS.Prompts.{name}.prompty")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Renders_the_shipped_default_when_nothing_is_overridden()
    {
        Assert.Equal(DefaultClock, Library().Render("clock", Clock));
    }

    [Fact]
    public void An_override_file_replaces_the_default()
    {
        _overrides.Files["clock.prompty"] = "---\nname: clock\n---\nIt is {{today}} in {{timeZone}}.";

        Assert.Equal("It is 2026-09-23 in Asia/Kolkata.", Library().Render("clock", Clock));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void A_broken_override_falls_back_to_the_default_and_says_why()
    {
        // A typo in a hand-edited file must not take chat down.
        _overrides.Files["clock.prompty"] = "It is {{todya}}.";

        Assert.Equal(DefaultClock, Library().Render("clock", Clock));
        var (level, message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("unknown variable 'todya'", message);
    }

    [Fact]
    public void Output_is_trimmed_so_fragments_join_cleanly()
    {
        _overrides.Files["clock.prompty"] = "\n\n  Today: {{today}}  \n\n";

        Assert.Equal("Today: 2026-09-23", Library().Render("clock", Clock));
    }

    [Fact]
    public void A_variant_is_used_where_one_ships_and_the_plain_fragment_otherwise()
    {
        var compactRules = Library().Render("tool-rules", Clock, "compact");
        var clock = Library().Render("clock", Clock, "compact");

        Assert.Equal(PromptLibrary.Default("tool-rules.compact").Render(Clock).Trim(), compactRules);
        Assert.Equal(DefaultClock, clock);
    }

    [Fact]
    public void An_installation_can_add_a_variant_that_was_never_shipped()
    {
        _overrides.Files["clock.compact.prompty"] = "Today: {{today}}";

        Assert.Equal("Today: 2026-09-23", Library().Render("clock", Clock, "compact"));
        Assert.Equal(DefaultClock, Library().Render("clock", Clock));
    }

    [Fact]
    public void Asking_for_a_fragment_that_was_never_shipped_is_a_bug()
    {
        Assert.Throws<InvalidOperationException>(() => Library().Render("no-such-fragment", Clock));
    }
}
