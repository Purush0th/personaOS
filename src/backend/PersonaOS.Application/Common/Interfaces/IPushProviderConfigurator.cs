namespace PersonaOS.Application.Common.Interfaces;

/// <summary>
/// Points the live push sender at a provider credential — or turns push off with null.
///
/// Push is configured from the admin page at runtime, not from a file at startup, so the
/// sender has to change while the API runs. This is the one place that changes it; callers
/// persist the credential first and then apply it.
/// </summary>
public interface IPushProviderConfigurator
{
    /// <summary>Uses <paramref name="serviceAccountJson"/> for future sends, or disables push when null.</summary>
    void Configure(string? serviceAccountJson);
}

/// <summary>
/// Data Protection purposes, one per kind of secret. A ciphertext produced under one purpose
/// cannot be decrypted under another, so a stored AI key can never be passed off as a push
/// credential or the other way round.
/// </summary>
public static class SecretPurposes
{
    /// <summary>Keyed-service name for the protector that guards the Firebase service account.</summary>
    public const string PushCredentials = "PersonaOS.FcmServiceAccount.v1";
}
