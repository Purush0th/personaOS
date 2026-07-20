using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Configuration;

public class InstanceConfigService(
    IAppDbContext db,
    ISecretProtector secretProtector) : IInstanceConfigService
{
    public async Task<InstanceConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        var config = await db.InstanceConfig.FirstOrDefaultAsync(ct);
        if (config is not null)
            return config;

        config = new InstanceConfig
        {
            Id = InstanceConfig.SingletonId,
            IsConfigured = false,
            Features = InstanceConfig.DefaultFeatures(),
        };
        db.InstanceConfig.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task UpdateAsync(Action<InstanceConfig> apply, CancellationToken ct = default)
    {
        var config = await GetOrCreateAsync(ct);
        apply(config);
        config.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAnthropicApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));

        var cipher = secretProtector.Protect(apiKey);
        await UpdateAsync(c => c.AnthropicApiKeyEncrypted = cipher, ct);
    }

    public async Task<string?> GetAnthropicApiKeyAsync(CancellationToken ct = default)
    {
        var config = await GetOrCreateAsync(ct);
        if (string.IsNullOrEmpty(config.AnthropicApiKeyEncrypted))
            return null;

        return secretProtector.Unprotect(config.AnthropicApiKeyEncrypted);
    }
}
