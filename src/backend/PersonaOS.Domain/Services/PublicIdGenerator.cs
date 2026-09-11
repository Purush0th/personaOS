using System.Security.Cryptography;

namespace PersonaOS.Domain.Services;

/// <summary>
/// Short, opaque, URL-safe identifiers for things that appear in a browser address bar.
///
/// Conversations are addressed by one of these rather than by their title, so nothing about
/// what was discussed leaks into browser history, bookmarks, or a proxy log — and rather than
/// by their sequential row id, which would advertise how many conversations exist.
/// </summary>
public static class PublicIdGenerator
{
    /// <summary>
    /// Lowercase letters and digits, minus the characters people misread when copying a link
    /// by hand: 0/o, 1/l, and i. 31 symbols over 8 places is ~10^11 combinations, ample for
    /// a single user's conversations.
    /// </summary>
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public const int Length = 8;

    /// <summary>A new random id. Cryptographically random, so ids are not guessable in sequence.</summary>
    public static string Next()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
