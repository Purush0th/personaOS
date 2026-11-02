using System.Text.RegularExpressions;

namespace PersonaOS.Application.Reminders;

/// <summary>
/// Reads a message that plainly cancels one reminder by what it is about: "Cancel my reminder to
/// call the bank", "Forget the reminder about the dentist", "Delete my laundry reminder". Used when
/// the model answered with no card, asking for an id the user cannot know (accuracy suite,
/// 2026-11-02). The card still names the reminder it found, for the user to confirm.
/// </summary>
public static partial class ReminderCommand
{
    [GeneratedRegex(
        @"^\s*(?:please\s+)?(?:cancel|forget|delete|remove|drop|clear|stop)\s+(?:my|the|that)\s+reminders?\s+(?:to|about|for|of|that)\s+(?<words>.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CancelAbout();

    [GeneratedRegex(
        @"^\s*(?:please\s+)?(?:cancel|forget|delete|remove|drop|clear|stop)\s+(?:my|the|that)\s+(?<words>.+?)\s+reminder[\s?.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CancelNamed();

    /// <summary>The words that name the reminder to cancel, or null when the message does not plainly cancel one.</summary>
    public static string? CancelWords(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = CancelAbout().Match(message);
        if (!match.Success) match = CancelNamed().Match(message);
        if (!match.Success) return null;
        var words = match.Groups["words"].Value.Trim();
        return words.Length == 0 || words.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : words;
    }
}
