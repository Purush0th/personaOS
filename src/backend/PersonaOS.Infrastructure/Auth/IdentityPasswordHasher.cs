using Microsoft.AspNetCore.Identity;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Auth;

/// <summary>
/// Password-hashing adapter over ASP.NET Identity's PBKDF2 hasher.
/// The TUser type parameter is unused by the algorithm; a dummy marker suffices.
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private sealed class Marker;

    private static readonly PasswordHasher<Marker> Hasher = new();
    private static readonly Marker User = new();

    public string Hash(string password) => Hasher.HashPassword(User, password);

    public PasswordVerifyResult Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(User, hash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerifyResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerifyResult.SuccessRehashNeeded,
            _ => PasswordVerifyResult.Failed,
        };
}
