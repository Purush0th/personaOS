using System.Runtime.CompilerServices;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.LocalModels;

/// <summary>
/// The model this server version recommends for a local install: the fine-tuned PersonaOS model,
/// pinned per release so an update never downloads a model that was not tested with it. Bound
/// from the "LocalModel" configuration section; the defaults below are what ships.
/// </summary>
public sealed class LocalModelOptions
{
    public const string SectionName = "LocalModel";

    /// <summary>What Ollama is asked to pull: the model on Hugging Face.</summary>
    public string Reference { get; set; } = "hf.co/purush0th/personaos-qwen3-4b-instruct-GGUF:Q4_K_M";

    /// <summary>
    /// The short name the model is given in Ollama once downloaded, and the name chat uses. Ollama
    /// names a Hugging Face pull after its repository and offers no way to choose otherwise.
    /// </summary>
    public string Name { get; set; } = "personaos-qwen3:4b";

    public string DisplayName { get; set; } = "PersonaOS model (Qwen3 4B, fine-tuned)";

    /// <summary>The download size, shown before the owner agrees to it.</summary>
    public long SizeBytes { get; set; } = 2_497_280_160;

    public string License { get; set; } = "Apache-2.0";

    public string LicenseUrl { get; set; } = "https://www.apache.org/licenses/LICENSE-2.0";

    public string Notice { get; set; } =
        "Fine-tuned from Qwen3-4B-Instruct-2507 by the PersonaOS project; not made or endorsed by Qwen or Alibaba Cloud. "
        + "It can be wrong: check anything important.";
}

/// <summary>What the Setup Wizard and Settings show about the recommended local model.</summary>
/// <param name="Found">A local model server answered.</param>
/// <param name="Address">Where it answered (its root), or the address that was tried.</param>
/// <param name="Model">The model's short name in Ollama, which chat uses.</param>
/// <param name="Installed">The server already has the model under that name.</param>
public sealed record LocalModelStatus(
    bool Found,
    string? Address,
    string? Version,
    string Model,
    string DisplayName,
    long SizeBytes,
    string License,
    string LicenseUrl,
    string Notice,
    bool Installed,
    string Message);

/// <summary>Finds the owner's Ollama, says whether it has the recommended model, and downloads it.</summary>
public interface ILocalModelService
{
    /// <summary>
    /// Looks for Ollama at <paramref name="address"/> when given (a "/v1" suffix is fine), else at
    /// the stored base URL, else at the usual places: the compose service, this machine, the Docker host.
    /// </summary>
    Task<LocalModelStatus> StatusAsync(string? address, CancellationToken ct = default);

    /// <summary>
    /// Downloads the recommended model into the Ollama at <paramref name="address"/> (skipped when it
    /// is already there under its Hugging Face name), then gives it its short name.
    /// </summary>
    IAsyncEnumerable<ModelPullProgress> PullAsync(string address, CancellationToken ct = default);
}

public sealed class LocalModelService(
    ILocalModelHost host, IInstanceConfigService configService, LocalModelOptions options) : ILocalModelService
{
    /// <summary>Where Ollama usually is: the compose service, this machine, and the Docker host.</summary>
    public static readonly string[] UsualAddresses = ["http://ollama:11434", "http://localhost:11434", "http://host.docker.internal:11434"];

    public async Task<LocalModelStatus> StatusAsync(string? address, CancellationToken ct = default)
    {
        foreach (var candidate in await CandidatesAsync(address, ct))
        {
            if (await host.DetectAsync(candidate, ct) is not { } found) continue;

            var installed = await host.HasModelAsync(found.Address, options.Name, ct);
            return Status(found.Address, found.Version, installed, installed
                ? $"Ollama {found.Version} at {found.Address} already has the model."
                : $"Ollama {found.Version} found at {found.Address}.");
        }

        var tried = string.IsNullOrWhiteSpace(address) ? null : RootOf(address);
        return Status(tried, null, false, tried is null
            ? "Ollama was not found. Install it (or start the compose profile \"ollama\"), then look again, or enter its address."
            : $"Nothing answered as Ollama at {tried}.");
    }

    public async IAsyncEnumerable<ModelPullProgress> PullAsync(string address, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new LocalModelHostException("Give the address of Ollama to download the model into.");
        var root = RootOf(address);
        if (await host.DetectAsync(root, ct) is null)
            throw new LocalModelHostException($"Nothing answered as Ollama at {root}.");

        if (!await host.HasModelAsync(root, options.Reference, ct))
        {
            await foreach (var step in host.PullAsync(root, options.Reference, ct))
                yield return step;
        }

        if (string.Equals(options.Name, options.Reference, StringComparison.OrdinalIgnoreCase)) yield break;
        yield return new ModelPullProgress($"naming it {options.Name}", null, null);
        await host.CopyModelAsync(root, options.Reference, options.Name, ct);
        yield return new ModelPullProgress("success", null, null);
    }

    /// <summary>"http://host:11434/v1/" is "http://host:11434": the native API lives at the root.</summary>
    public static string RootOf(string address)
    {
        var root = address.Trim().TrimEnd('/');
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) root = root[..^3];
        return root.TrimEnd('/');
    }

    private async Task<IReadOnlyList<string>> CandidatesAsync(string? address, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(address)) return [RootOf(address)];

        var config = await configService.GetOrCreateAsync(ct);
        var stored = InstanceConfig.Providers.NeedsBaseUrl(config.AiProvider) && !string.IsNullOrWhiteSpace(config.AiBaseUrl)
            ? RootOf(config.AiBaseUrl)
            : null;
        return [.. new[] { stored }.Concat(UsualAddresses).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private LocalModelStatus Status(string? address, string? version, bool installed, string message) =>
        new(version is not null, address, version, options.Name, options.DisplayName, options.SizeBytes,
            options.License, options.LicenseUrl, options.Notice, installed, message);
}
