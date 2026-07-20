using Microsoft.AspNetCore.DataProtection;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Security;

/// <summary>
/// Secret encryption via ASP.NET Data Protection. The purpose string must never
/// change — existing installs' ciphertexts are bound to it.
/// </summary>
public class DataProtectionSecretProtector : ISecretProtector
{
    private const string ProtectorPurpose = "PersonaOS.AnthropicApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
