namespace PersonaOS.Application.Ai;

/// <summary>
/// A tool's name in the user's words. Tool names are internals ("get_goals"), yet the chat showed
/// them: "Using get_goals…" while a tool ran and <c>get_goals</c> in the list of what was done.
/// Three forms, one per place:
/// <list type="bullet">
/// <item><see cref="Action"/> on a confirmation card: "Create goal".</item>
/// <item><see cref="Running"/> while it runs: "Reading goals…".</item>
/// <item><see cref="Done"/> once it has: "Created goal".</item>
/// </list>
/// Built from the name (verb first, then what it acts on), so a new tool reads sensibly without
/// being registered here; only its verb needs to be one of <see cref="Verbs"/> to get the tenses.
/// </summary>
public static class ToolLabel
{
    /// <summary>A tool name's first word, as a command, while running and once done.</summary>
    private static readonly Dictionary<string, (string Action, string Running, string Done)> Verbs = new()
    {
        ["get"] = ("Read", "Reading", "Read"),
        ["list"] = ("List", "Listing", "Listed"),
        ["read"] = ("Read", "Reading", "Read"),
        ["create"] = ("Create", "Creating", "Created"),
        ["update"] = ("Update", "Updating", "Updated"),
        ["move"] = ("Move", "Moving", "Moved"),
        ["delete"] = ("Delete", "Deleting", "Deleted"),
        ["cancel"] = ("Cancel", "Cancelling", "Cancelled"),
        ["start"] = ("Start", "Starting", "Started"),
        ["complete"] = ("Complete", "Completing", "Completed"),
        ["remember"] = ("Remember", "Remembering", "Remembered"),
    };

    public static string Action(string toolName) => Phrase(toolName, v => v.Action);

    public static string Running(string toolName) => Phrase(toolName, v => v.Running) + "…";

    public static string Done(string toolName) => Phrase(toolName, v => v.Done);

    private static string Phrase(string toolName, Func<(string Action, string Running, string Done), string> tense)
    {
        var words = toolName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "Run a tool";

        // What it acts on, spoken to the user: remember_about_user is "about you".
        var rest = string.Join(' ', words.Skip(1).Select(w => w == "user" ? "you" : w));
        if (Verbs.TryGetValue(words[0], out var verb)) return rest.Length == 0 ? tense(verb) : $"{tense(verb)} {rest}";

        var plain = string.Join(' ', words);
        return char.ToUpperInvariant(plain[0]) + plain[1..];
    }
}
