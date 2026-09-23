using Microsoft.Extensions.Configuration;
using PersonaOS.Infrastructure.Ai;

namespace PersonaOS.Tests.Ai.Prompts;

public sealed class FilePromptOverrideSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("personaos-prompts-").FullName;

    private FilePromptOverrideSource Source() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Prompts:OverridePath"] = _dir })
        .Build());

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_an_override_that_exists()
    {
        File.WriteAllText(Path.Combine(_dir, "tool-rules.prompty"), "custom rules");

        Assert.Equal("custom rules", Source().Read("tool-rules.prompty"));
    }

    [Fact]
    public void Returns_nothing_for_a_fragment_that_was_not_overridden()
    {
        Assert.Null(Source().Read("identity.prompty"));
    }

    [Fact]
    public void Picks_up_an_edit_without_a_restart()
    {
        var source = Source();
        var path = Path.Combine(_dir, "clock.prompty");

        File.WriteAllText(path, "first");
        Assert.Equal("first", source.Read("clock.prompty"));
        File.WriteAllText(path, "second");
        Assert.Equal("second", source.Read("clock.prompty"));
    }

    [Theory]
    [InlineData("../secrets.prompty")]
    [InlineData("..\\secrets.prompty")]
    [InlineData("/etc/passwd")]
    [InlineData("identity.txt")]
    [InlineData("Identity.prompty")]
    public void Refuses_anything_but_a_plain_fragment_file_name(string fileName)
    {
        Assert.Throws<ArgumentException>(() => Source().Read(fileName));
    }
}
