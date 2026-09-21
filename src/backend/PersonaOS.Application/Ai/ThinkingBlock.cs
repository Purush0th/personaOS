using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Removes a reasoning block a model wrote into its reply.
///
/// Qwen3 and other thinking models mark reasoning with &lt;think&gt;…&lt;/think&gt;. Providers
/// usually return it in a field of its own, which the adapters drop, but not always: served
/// through an OpenAI-compatible endpoint it can arrive as ordinary content, and then the user
/// reads the model's private notes and the app stores them as the reply.
///
/// The prompt asks Qwen3 not to think at all; this is the net under that, for every model and
/// every provider. An unclosed opening tag takes the rest of the text with it, because a stream
/// that was cut mid-thought has no answer in it either.
/// </summary>
public static partial class ThinkingBlock
{
    [GeneratedRegex(@"<(think|thinking|thought|reasoning)>.*?</\1>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Closed();

    [GeneratedRegex(@"<(think|thinking|thought|reasoning)>.*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Unclosed();

    /// <summary>
    /// Returns <paramref name="text"/> without its reasoning blocks, or the same string when
    /// there are none.
    /// </summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (!text.Contains('<')) return text;

        var stripped = Closed().Replace(text, string.Empty);
        stripped = Unclosed().Replace(stripped, string.Empty);

        // A reply that was nothing but reasoning comes back empty on purpose: the model never
        // answered, and the caller says so rather than showing the user its notes.
        return stripped.Trim();
    }
}
