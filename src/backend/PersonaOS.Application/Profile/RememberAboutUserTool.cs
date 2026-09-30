using System.Text.Json;
using PersonaOS.Application.Ai.Tools;

namespace PersonaOS.Application.Profile;

/// <summary>
/// Lets the assistant add a lasting fact to what it knows about the user ("I'm vegetarian",
/// "call me Sam"). Like every write it becomes a card, so nothing is remembered that the user
/// did not agree to, and the text stays editable in Settings.
/// </summary>
public class RememberAboutUserTool(IUserProfileService profile) : IPersonaTool
{
    public string Name => "remember_about_user";

    public string Description =>
        "Adds one line to the user's About you profile, which goes with every message. Only when the " +
        "user asks to add something to their profile or About you (e.g. \"add to my profile that I am " +
        "vegetarian\"). Anything else worth remembering goes to create_memory when that tool is offered.";

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "The fact, as one short sentence." }
          },
          "required": ["message"]
        }
        """;

    public string? RequiredFeature => null;

    /// <summary>
    /// Only while the memory module is off. With both offered, qwen2.5:3b put "prefers morning
    /// workouts" into About you instead of saving a memory (2026-09-29): two tools for one job is
    /// one too many for a small model. About you stays editable by hand in Settings.
    /// </summary>
    public bool IsOffered(PersonaOS.Domain.Entities.InstanceConfig config) =>
        !config.IsEnabled(PersonaOS.Domain.Entities.InstanceConfig.Modules.Memory);

    public Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        Fact(input).Length == 0
            ? throw new ProfileValidationException("'message' is required: the fact to remember.")
            : Task.CompletedTask;

    public async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var saved = await profile.RememberAsync(Fact(input), ct);
        return ToolJson.Serialize(new { remembered = Fact(input), aboutMe = saved.AboutMe });
    }

    private static string Fact(JsonElement input) =>
        input.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
