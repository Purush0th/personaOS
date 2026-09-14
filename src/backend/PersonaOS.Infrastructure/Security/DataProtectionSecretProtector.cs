using Microsoft.AspNetCore.DataProtection;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Security;

/// <summary>
/// Secret encryption via ASP.NET Data Protection, one purpose per kind of secret. A purpose
/// string must never change once used — existing installs' ciphertexts are bound to it.
/// </summary>
public class DataProtectionSecretProtector : ISecretProtector
{
    /// <summary>
    /// The AI provider key's purpose. Named for Anthropic for historical reasons, but it guards
    /// whichever provider's key is stored; renaming it would make every existing key unreadable.
    /// </summary>
    public const string AiProviderKeyPurpose = "PersonaOS.AnthropicApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        : this(provider, AiProviderKeyPurpose)
    {
    }

    public DataProtectionSecretProtector(IDataProtectionProvider provider, string purpose)
    {
        _protector = provider.CreateProtector(purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
