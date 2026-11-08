namespace PersonaOS.Application.Common;

/// <summary>
/// A crude English stem, so a question finds a title with another ending of the same word:
/// "commuting" and "Commute", "workouts" and "workout", "running" and "run", "Meditation's" and
/// "meditation". Both sides go through it, so a rough stem is enough; it needs no dictionary.
/// </summary>
public static class WordStem
{
    public static string Of(string word)
    {
        var w = word.ToLowerInvariant();
        if (w.EndsWith("'s", StringComparison.Ordinal)) w = w[..^2];
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) w = w[..^1];

        // "commuting", "commuted": keep at least three letters, so "ring" and "bed" stay whole.
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) w = Undouble(w[..^3]);
        else if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal) && !w.EndsWith("eed", StringComparison.Ordinal)) w = Undouble(w[..^2]);

        // "commute" and "commut(ing)" meet at "commut".
        if (w.Length > 3 && w.EndsWith('e') && !w.EndsWith("ee", StringComparison.Ordinal)) w = w[..^1];
        return w;
    }

    /// <summary>"runn" to "run", "shopp" to "shop"; "fill" and "pass" are left alone.</summary>
    private static string Undouble(string w) =>
        w.Length >= 4 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z' or 'f') ? w[..^1] : w;
}
