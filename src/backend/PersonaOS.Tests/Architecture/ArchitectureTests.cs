using System.Reflection;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Infrastructure.Persistence;

namespace PersonaOS.Tests.Architecture;

/// <summary>
/// The dependency rule (docs/ENGINEERING.md, Architecture), checked rather than remembered:
/// Domain depends on nothing; Application on Domain and abstractions only, never on
/// Infrastructure, EF's SQLite provider, a provider SDK or ASP.NET; Infrastructure never on Api;
/// and the Anthropic SDK is used in exactly one file.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(InstanceConfig).Assembly;
    private static readonly Assembly Application = typeof(IAppDbContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;

    private static IReadOnlyList<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    [Fact]
    public void Domain_depends_on_nothing_but_the_runtime()
    {
        var outside = References(Domain).Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard");

        Assert.Empty(outside);
    }

    [Theory]
    [InlineData("PersonaOS.Infrastructure")]
    [InlineData("PersonaOS.Api")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("Anthropic")]
    [InlineData("FirebaseAdmin")]
    [InlineData("Microsoft.AspNetCore")]
    public void Application_does_not_depend_on(string forbidden)
    {
        var hits = References(Application).Where(n => n == forbidden || n.StartsWith(forbidden + ".", StringComparison.Ordinal));

        Assert.Empty(hits);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        Assert.DoesNotContain("PersonaOS.Api", References(Infrastructure));
    }

    [Fact]
    public void Only_one_file_touches_the_anthropic_sdk()
    {
        var backend = FindBackendFolder();
        var users = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadLines(f).Any(l => l.StartsWith("using Anthropic", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["AnthropicMessageStreamer.cs"], users);
    }

    [Fact]
    public void Controllers_reach_data_through_application_interfaces_only()
    {
        // Api is not referenced by this project (it is the composition root); its controllers are
        // read as source. A controller that takes the database, even through its port, does a use
        // case's work itself; it calls an Application service instead.
        var controllers = Path.Combine(FindBackendFolder(), "PersonaOS.Api", "Controllers");
        var offenders = Directory.EnumerateFiles(controllers, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("AppDbContext", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindBackendFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PersonaOS.Api", "PersonaOS.Api.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find src/backend above the test output.");
    }
}
