namespace PersonaOS.Application.Common.Interfaces;

public enum PasswordVerifyResult
{
    Failed,
    Success,
    /// <summary>Valid, but the hash was produced with outdated parameters — re-hash and store.</summary>
    SuccessRehashNeeded,
}

/// <summary>Password hashing port; implementation chooses the algorithm.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerifyResult Verify(string hash, string password);
}
