using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Strips the lead-in a model writes when it answers the automatic claim check as if it were a
/// person: seen on the phone as <c>"Sure, here is the corrected message:"</c> in front of the
/// reply the user then read. The check is internal, so nothing about it belongs in the reply.
///
/// The instruction sent with the check asks for the reply alone, which is the real fix; this
/// catches the models that answer conversationally anyway. It only removes a leading line that
/// is entirely about producing a corrected message, never ordinary prose.
/// </summary>
public static partial class CorrectionPreamble
{
    [GeneratedRegex(
        @"^\s*(?:(?:sure|certainly|okay|ok|of\s+course|got\s+it|understood|alright|apologies|sorry)\b[,.!…\-–—\s]*)*"
        + @"(?:(?:here(?:'s|’s|\s+is)|this\s+is|below\s+is)\s+)?"
        + @"(?:the|my|a|an)?\s*"
        + @"(?:corrected|revised|updated|rewritten|re-?written|amended|fixed|new)\s+"
        + @"(?:message|reply|response|version|text|wording)\s*[:\-–—]+\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex Preamble();

    /// <summary>
    /// Returns <paramref name="reply"/> without its correction lead-in. The same string comes
    /// back when there is nothing to strip, and the whole reply is kept when it is only a
    /// preamble — an empty reply would be worse than a clumsy one.
    /// </summary>
    public static string Strip(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return reply ?? string.Empty;

        var match = Preamble().Match(reply);
        if (!match.Success || match.Length == 0) return reply;

        var rest = reply[match.Length..].TrimStart();
        return rest.Length == 0 ? reply : rest;
    }
}
