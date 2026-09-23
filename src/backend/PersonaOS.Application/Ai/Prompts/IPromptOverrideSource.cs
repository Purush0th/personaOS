namespace PersonaOS.Application.Ai.Prompts;

/// <summary>
/// Where an installation's own edits to the prompt live. A fragment found here replaces the
/// shipped default of the same name; Infrastructure reads them from a directory on disk.
/// </summary>
public interface IPromptOverrideSource
{
    /// <summary>The override's text, or null when the fragment has not been overridden.</summary>
    string? Read(string fileName);
}

/// <summary>No overrides: every fragment is the shipped default. Used by tests and as the fallback.</summary>
public sealed class NoPromptOverrides : IPromptOverrideSource
{
    public string? Read(string fileName) => null;
}
