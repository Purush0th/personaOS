namespace PersonaOS.Application.Ai;

/// <summary>
/// Builds the assistant's system prompt from the install's <c>InstanceConfig</c>
/// (nickname + persona template) and the user's profile.
/// </summary>
public interface ISystemPromptBuilder
{
    Task<string> BuildAsync(PromptRequest? request = null, CancellationToken ct = default);
}

/// <summary>
/// What the prompt is for: the user's message picks the memories that come with it, and the
/// conversation's mode (<see cref="PersonaOS.Domain.Entities.ChatModes"/>) adds its instructions.
/// </summary>
public record PromptRequest(string? UserMessage = null, string? Mode = null);
