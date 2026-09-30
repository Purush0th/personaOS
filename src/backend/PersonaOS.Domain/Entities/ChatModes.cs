namespace PersonaOS.Domain.Entities;

/// <summary>
/// How the user wants the assistant to work in a conversation, picked with a switch in the chat
/// box. A mode changes the instructions and the tools offered, never the provider, and every mode
/// keeps the same guards, cards and receipts.
/// </summary>
public static class ChatModes
{
    /// <summary>Answer and discuss; changes go through cards. The default.</summary>
    public const string Chat = "chat";

    /// <summary>Explore options without changing anything: no write tools at all.</summary>
    public const string Brainstorm = "brainstorm";

    /// <summary>Turn an intent into a proposed set of goals and tasks, several cards at once.</summary>
    public const string Plan = "plan";

    /// <summary>Prepare the actions the user asked for, as cards, without discussing first.</summary>
    public const string Act = "act";

    /// <summary>Review progress and patterns: no write tools at all.</summary>
    public const string Reflect = "reflect";

    public static readonly IReadOnlyList<string> All = [Chat, Brainstorm, Plan, Act, Reflect];

    /// <summary>Whether the mode may change the user's data (always behind cards).</summary>
    public static bool Writes(string mode) => mode is not (Brainstorm or Reflect);

    /// <summary>A known mode, or null for anything else (missing, misspelt, from an older client).</summary>
    public static string? Parse(string? mode)
    {
        var value = mode?.Trim().ToLowerInvariant();
        return value is not null && All.Contains(value) ? value : null;
    }

    /// <summary>"brainstorm" reads "Brainstorm".</summary>
    public static string Label(string mode) => mode.Length == 0 ? mode : char.ToUpperInvariant(mode[0]) + mode[1..];
}
