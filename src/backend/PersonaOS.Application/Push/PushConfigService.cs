using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Application.Push;

/// <summary>Whether push is on, and for which Firebase project. Never carries the key.</summary>
public record PushConfigStatus(bool Configured, string? ProjectId);

public interface IPushConfigService
{
    Task<PushConfigStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates both Firebase files, stores them, and switches push on immediately. Throws
    /// <see cref="PushConfigValidationException"/> without changing anything if either is wrong.
    /// </summary>
    Task<PushConfigStatus> SetAsync(string serviceAccountJson, string googleServicesJson, CancellationToken ct = default);

    /// <summary>Removes the stored credential and turns push off.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>The options the mobile app starts Firebase with, or null when push is off.</summary>
    Task<FcmClientOptions?> GetClientOptionsAsync(CancellationToken ct = default);

    /// <summary>Decrypts the stored credential and applies it to the live sender. Run at startup.</summary>
    Task ApplyStoredAsync(CancellationToken ct = default);
}

/// <summary>
/// Bring-your-own Firebase. Each install uploads the files from its own Firebase project on
/// the admin page; nothing Firebase-related ships in the release APK or the image, so no
/// credential is ever shared between installs.
/// </summary>
public class PushConfigService(
    IInstanceConfigService configService,
    [FromKeyedServices(SecretPurposes.PushCredentials)] ISecretProtector protector,
    IPushProviderConfigurator pushConfigurator) : IPushConfigService
{
    public async Task<PushConfigStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return new PushConfigStatus(config.FcmServiceAccountEncrypted is not null, config.FcmProjectId);
    }

    public async Task<PushConfigStatus> SetAsync(
        string serviceAccountJson, string googleServicesJson, CancellationToken ct = default)
    {
        // Validate everything before touching storage, so a bad upload leaves a working
        // configuration exactly as it was.
        var projectId = FirebaseConfigParser.ReadServiceAccountProjectId(serviceAccountJson);
        var client = FirebaseConfigParser.ReadClientOptions(googleServicesJson);
        FirebaseConfigParser.EnsureSameProject(projectId, client);

        // Load the key into the live sender BEFORE saving it. A file can be shaped correctly and
        // still hold a private key that does not parse; applying first means that fails here,
        // with nothing stored — rather than persisting a key that then refuses to load, leaving
        // the database saying push is on while the running sender is still the old one.
        try
        {
            pushConfigurator.Configure(serviceAccountJson);
        }
        catch (Exception ex) when (ex is not PushConfigValidationException)
        {
            throw new PushConfigValidationException(
                $"The service account key could not be loaded ({ex.Message}). Generate a new key and try again.");
        }

        await configService.UpdateAsync(c =>
        {
            c.FcmServiceAccountEncrypted = protector.Protect(serviceAccountJson);
            c.FcmClientConfigJson = JsonSerializer.Serialize(client);
            c.FcmProjectId = projectId;
        }, ct);

        return new PushConfigStatus(true, projectId);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await configService.UpdateAsync(c =>
        {
            c.FcmServiceAccountEncrypted = null;
            c.FcmClientConfigJson = null;
            c.FcmProjectId = null;
        }, ct);

        pushConfigurator.Configure(null);
    }

    public async Task<FcmClientOptions?> GetClientOptionsAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return string.IsNullOrWhiteSpace(config.FcmClientConfigJson)
            ? null
            : JsonSerializer.Deserialize<FcmClientOptions>(config.FcmClientConfigJson);
    }

    public async Task ApplyStoredAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        pushConfigurator.Configure(
            config.FcmServiceAccountEncrypted is null ? null : protector.Unprotect(config.FcmServiceAccountEncrypted));
    }
}
