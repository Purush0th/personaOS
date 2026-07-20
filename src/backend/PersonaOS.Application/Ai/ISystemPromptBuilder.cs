namespace PersonaOS.Application.Ai;

/// <summary>
/// Builds the assistant's system prompt from the install's <c>InstanceConfig</c>
/// (nickname + persona template) and the user's profile.
/// </summary>
public interface ISystemPromptBuilder
{
    Task<string> BuildAsync(CancellationToken ct = default);
}
