using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using PersonaOS.Application.Ai.Prompts;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Reads prompt overrides from a directory ("Prompts:OverridePath", default ./data/prompts).
/// A file there named like a shipped fragment, e.g. <c>tool-rules.prompty</c>, replaces it.
///
/// Read on every request rather than cached, so an edit takes effect on the next message with no
/// restart. The directory does not have to exist; without it every fragment is the default.
/// </summary>
public sealed partial class FilePromptOverrideSource(IConfiguration configuration) : IPromptOverrideSource
{
    private readonly string _root = Path.GetFullPath(
        configuration["Prompts:OverridePath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "data", "prompts"));

    public string? Read(string fileName)
    {
        // Names come from code, but the guard keeps this from ever reading outside the directory.
        if (!SafeName().IsMatch(fileName)) throw new ArgumentException($"'{fileName}' is not a prompt file name.", nameof(fileName));

        var path = Path.Combine(_root, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*(\.[a-z0-9-]+)*\.prompty$")]
    private static partial Regex SafeName();
}
