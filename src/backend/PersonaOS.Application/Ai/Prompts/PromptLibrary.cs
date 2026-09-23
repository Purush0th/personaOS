using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PersonaOS.Application.Ai.Prompts;

/// <summary>
/// Renders prompt fragments by name. The shipped defaults are <c>.prompty</c> files embedded in
/// this assembly (<c>Ai/Prompts/Fragments</c>); an installation can replace any of them with a
/// file of the same name, read through <see cref="IPromptOverrideSource"/>.
///
/// An override that fails to parse or render (a typo in a tag, a variable that does not exist) is
/// logged and skipped in favour of the default: a hand edit must never take chat down.
/// </summary>
public interface IPromptLibrary
{
    /// <summary>Renders the fragment, trimmed. Empty when the fragment renders to nothing.</summary>
    string Render(string fragment, IReadOnlyDictionary<string, object?> values);
}

public sealed class PromptLibrary(IPromptOverrideSource overrides, ILogger<PromptLibrary> logger) : IPromptLibrary
{
    private const string ResourcePrefix = "PersonaOS.Prompts.";
    private const string Extension = ".prompty";

    private static readonly ConcurrentDictionary<string, PromptTemplate> Defaults = new();

    public string Render(string fragment, IReadOnlyDictionary<string, object?> values)
    {
        var fileName = fragment + Extension;
        var overrideText = overrides.Read(fileName);
        if (overrideText is not null)
        {
            try
            {
                return PromptTemplate.Parse(fragment, overrideText).Render(values).Trim();
            }
            catch (PromptTemplateException e)
            {
                logger.LogWarning("Ignoring the override for prompt fragment {Fragment} and using the default: {Problem}",
                    fragment, e.Message);
            }
        }

        return Default(fragment).Render(values).Trim();
    }

    /// <summary>The shipped fragment. Throws for a name that does not exist: that is a bug here, not a user edit.</summary>
    public static PromptTemplate Default(string fragment) => Defaults.GetOrAdd(fragment, name =>
    {
        using var stream = typeof(PromptLibrary).Assembly.GetManifestResourceStream(ResourcePrefix + name + Extension)
            ?? throw new InvalidOperationException($"No shipped prompt fragment named '{name}'.");
        using var reader = new StreamReader(stream);
        return PromptTemplate.Parse(name, reader.ReadToEnd());
    });

    /// <summary>Every shipped fragment's name, for tests and for documenting what can be overridden.</summary>
    public static IReadOnlyList<string> DefaultNames() => typeof(PromptLibrary).Assembly
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(Extension, StringComparison.Ordinal))
        .Select(n => n[ResourcePrefix.Length..^Extension.Length])
        .Order(StringComparer.Ordinal)
        .ToList();
}
