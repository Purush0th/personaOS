namespace PersonaOS.Application.Common.Interfaces;

/// <summary>Encrypts/decrypts secrets at rest (e.g. the Anthropic API key).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
