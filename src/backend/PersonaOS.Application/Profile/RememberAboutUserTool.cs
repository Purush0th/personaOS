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
        "Saves one lasting fact or preference about the user, in their words, so every future " +
        "conversation knows it (e.g. \"is vegetarian\", \"prefers short answers\", \"lives in Lisbon\"). " +
        "Only for things the user said about themselves and wants kept; not for tasks, goals or reminders.";

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
