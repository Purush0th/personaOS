using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai;

/// <summary>
/// A plain sum asked in words: "What is 15% of 80?", "What's 23 times 4?", "How much is 120 / 8?".
/// qwen2.5:3b answered 20 and 5 for "15% of 80" and 40 for "20% of 250" (holdout2 and holdout3,
/// 2026-11-02): a small model guesses numbers. The sum is worked out here and the reply checked
/// against it; a reply that does not give the right number is replaced.
/// </summary>
public static partial class Arithmetic
{
    /// <summary>The sum as the user wrote it and its value.</summary>
    public sealed record Sum(string Question, decimal Value)
    {
        public string Answer => Format(Value);
    }

    [GeneratedRegex(@"^\s*(?:(?:so|ok|okay|hey|quick question)[,\s]+)?(?:what(?:'s|s|\s+is|\s+are)|how\s+much\s+is|calculate|compute|work\s+out|tell\s+me)\s+(?<sum>.+?)[\s?.!=]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Question();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?:%|percent|per\s+cent)\s+of\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PercentOf();

    [GeneratedRegex(@"^[\d.\s+\-*/()]+$")]
    private static partial Regex OnlySum();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"-?\d{1,3}(?:,\d{3})+(?:\.\d+)?|-?\d+(?:\.\d+)?")]
    private static partial Regex NumberInReply();

    /// <summary>The sum the message asks for, or null when it is not a plain sum of numbers.</summary>
    public static Sum? Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || Question().Match(message) is not { Success: true } match) return null;
        var asked = match.Groups["sum"].Value.Trim();
        // "What is 2026-11-02?" names a day, not a subtraction.
        if (Regex.IsMatch(asked, @"\d{4}-\d{1,2}-\d{1,2}|\d{1,2}/\d{1,2}/\d{2,4}")) return null;

        var sum = PercentOf().Replace(asked, m => $"({m.Groups["n"].Value} / 100) * ");
        foreach (var (words, symbol) in Words)
            sum = Regex.Replace(sum, words, symbol, RegexOptions.IgnoreCase);
        sum = sum.Replace(",", string.Empty, StringComparison.Ordinal);

        if (!OnlySum().IsMatch(sum) || Number().Count(sum) < 2 || !sum.Any(c => c is '+' or '-' or '*' or '/')) return null;
        return Evaluate(sum) is decimal value ? new Sum(asked, value) : null;
    }

    /// <summary>Whether the reply gives the value: some number in it equals the sum's, to the cent.</summary>
    public static bool Says(string reply, decimal value) =>
        NumberInReply().Matches(reply).Any(m =>
            decimal.TryParse(m.Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            && Math.Abs(n - value) < 0.005m);

    private static readonly (string Words, string Symbol)[] Words =
    [
        (@"\bmultiplied\s+by\b", "*"),
        (@"\bdivided\s+by\b", "/"),
        (@"\btimes\b", "*"),
        (@"(?<=\d)\s*[x×]\s*(?=\d)", "*"),
        (@"÷", "/"),
        (@"\bover\b", "/"),
        (@"\bplus\b", "+"),
        (@"\bminus\b", "-"),
        (@"\band\b", "+"),
    ];

    private static string Format(decimal value) =>
        Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>The value of an expression of numbers, + - * / and brackets; null when it does not read or divides by zero.</summary>
    private static decimal? Evaluate(string text)
    {
        var tokens = Regex.Matches(text, @"\d+(?:\.\d+)?|[+\-*/()]").Select(m => m.Value).ToList();
        var at = 0;
        try
        {
            var value = Expression(tokens, ref at);
            return at == tokens.Count ? value : null;
        }
        catch (Exception e) when (e is FormatException or DivideByZeroException or OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static decimal Expression(List<string> tokens, ref int at)
    {
        var value = Term(tokens, ref at);
        while (at < tokens.Count && tokens[at] is "+" or "-")
        {
            var op = tokens[at++];
            var right = Term(tokens, ref at);
            value = op == "+" ? value + right : value - right;
        }
        return value;
    }

    private static decimal Term(List<string> tokens, ref int at)
    {
        var value = Factor(tokens, ref at);
        while (at < tokens.Count && tokens[at] is "*" or "/")
        {
            var op = tokens[at++];
            var right = Factor(tokens, ref at);
            value = op == "*" ? value * right : value / right;
        }
        return value;
    }

    private static decimal Factor(List<string> tokens, ref int at)
    {
        var token = tokens[at++];
        if (token == "-") return -Factor(tokens, ref at);
        if (token == "(")
        {
            var value = Expression(tokens, ref at);
            if (tokens[at++] != ")") throw new FormatException("unclosed bracket");
            return value;
        }
        return decimal.Parse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}
