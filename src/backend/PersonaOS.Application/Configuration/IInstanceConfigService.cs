using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Configuration;

public interface IInstanceConfigService
{
    /// <summary>Returns the singleton config, creating a default unconfigured row if none exists.</summary>
    Task<InstanceConfig> GetOrCreateAsync(CancellationToken ct = default);

    /// <summary>Persists personalization from the Setup Wizard (nickname, persona, model, tz, features).</summary>
    Task UpdateAsync(Action<InstanceConfig> apply, CancellationToken ct = default);

    /// <summary>Stores the Anthropic API key encrypted at rest.</summary>
    Task SetAnthropicApiKeyAsync(string apiKey, CancellationToken ct = default);

    /// <summary>Returns the decrypted Anthropic API key, or null if none is set.</summary>
    Task<string?> GetAnthropicApiKeyAsync(CancellationToken ct = default);
}
