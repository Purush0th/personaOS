using System.Text.RegularExpressions;

namespace PersonaOS.Application.Board;

/// <summary>
/// Reads a chat message that plainly asks for a task: "Create a to-do for following up with Ravi",
/// "Add a task to call the bank", "Put 'renew the passport' on my board". Used when the model
/// called create_task with no title: qwen2.5:3b did that for "Create a to-do for following up with
/// Ravi", then asked the user for a title they had just given (accuracy suite, 2026-11-02).
/// </summary>
public static partial class TaskRequest
{
    [GeneratedRegex(
        @"^\s*(?:(?:hey|ok|okay)[,\s]+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?"
        + @"(?:add|create|make|put|log|open)\s+(?:a\s+|an\s+)?(?:new\s+)?(?:task|to-?do|todo|item|ticket)"
        + @"(?:\s+(?:to|in|on)\s+(?:my|the)\s+(?:board|backlog|list|tasks))?"
        + @"\s*(?:to|for|about|that|:|-|–|saying|called|named)?\s+(?<text>.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AsksForATask();

    [GeneratedRegex(
        @"^\s*(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:add|put)\s+(?<q>['""“‘])(?<text>[^'""”’]+)['""”’]\s+(?:on|to|in)\s+(?:my|the)\s+(?:board|backlog|list|tasks)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PutsOnTheBoard();

    /// <summary>
    /// "Add repaint the hallway to my to-do list please", "Can you add X to my backlog?": the place
    /// named last, with no quotes. qwen2.5:3b made these calendar events, a plan or a sprint (training
    /// capture, 2026-11-02).
    /// </summary>
    [GeneratedRegex(
        @"^\s*(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:add|put|stick)\s+(?<text>.+?)\s+(?:on|to|in|onto|into)\s+(?:my|the)\s+"
        + @"(?:board|backlog|to-?do(?:\s+list)?|todo(?:\s+list)?|task\s+list|tasks)(?:\s*,?\s*please)?[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AddsToAPlace();

    /// <summary>"to my backlog" left over, or nothing but a place: no title in it.</summary>
    [GeneratedRegex(@"^(?:to\s+|in\s+|on\s+)?(?:my|the)\s+(?:board|backlog|list|tasks|sprint)$", RegexOptions.IgnoreCase)]
    private static partial Regex OnlyAPlace();

    /// <summary>"Make a task", "New task please", "Could you add a task to my board?": a task and nothing about it.</summary>
    [GeneratedRegex(
        @"^\s*(?:(?:hey|ok|okay)[,\s]+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:(?:add|create|make|open|log)\s+(?:me\s+)?(?:a\s+|an\s+)?(?:new\s+)?|new\s+)"
        + @"(?:task|to-?do|todo|ticket)(?:\s+(?:to|in|on)\s+(?:my|the)\s+(?:board|backlog|list|tasks))?(?:\s+for\s+me)?(?:\s*,?\s*please)?[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareTask();

    /// <summary>Whether the message asks for a task without saying what it is, so the only right reply is a question.</summary>
    public static bool AsksWithoutTitle(string? message) => !string.IsNullOrWhiteSpace(message) && BareTask().IsMatch(message);

    /// <summary>A question, a no, or a change of mind: not a title.</summary>
    [GeneratedRegex(@"\?|^\s*(?:no|nope|cancel|stop|never\s*mind|forget\s+it|don'?t|what|why|how|who|when|where|which|can|could|would|will|is|are|do|does)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotATitle();

    /// <summary>
    /// The title given in answer to being asked for one: "Add a task to my backlog", then "Book the
    /// car service". The trained qwen2.5:3b answered the second with "I couldn't create the task.
    /// What should it be?" once in five (training run 2026-11-06b, bench-8). Null unless the user's
    /// last message asked for a task with nothing said about it and this one is a short plain phrase.
    /// </summary>
    public static string? TitleAfterAsking(string? message, IReadOnlyList<string> earlierUserMessages)
    {
        if (string.IsNullOrWhiteSpace(message) || earlierUserMessages.Count == 0 || !AsksWithoutTitle(earlierUserMessages[^1]))
            return null;
        var text = message.Trim().Trim('\'', '"', '“', '”', '‘', '’').TrimEnd('.', '!').Trim();
        if (text.Length < 3 || NotATitle().IsMatch(text) || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) return null;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>The task's title in the user's words, starting with a capital; null when the message does not plainly give one.</summary>
    public static string? TitleOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = PutsOnTheBoard().Match(message);
        if (!match.Success) match = AsksForATask().Match(message);
        if (!match.Success) match = AddsToAPlace().Match(message);
        if (!match.Success) return null;

        var text = match.Groups["text"].Value.Trim().Trim('\'', '"', '“', '”', '‘', '’').Trim();
        text = Regex.Replace(text, @"\s+(?:to|in|on)\s+(?:my|the)\s+(?:board|backlog|list|tasks)$", string.Empty, RegexOptions.IgnoreCase);
        if (text.Length == 0 || OnlyAPlace().IsMatch(text)) return null;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
