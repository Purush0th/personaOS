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
