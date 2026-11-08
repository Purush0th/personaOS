namespace PersonaOS.Application.Common.Interfaces;

/// <summary>A local model server that answered at <paramref name="Address"/> (its root, no "/v1").</summary>
public sealed record LocalHostInfo(string Address, string Version);

/// <summary>One step of a model download: what the host is doing, and how far, in bytes, when it says.</summary>
public sealed record ModelPullProgress(string Status, long? Completed, long? Total);

/// <summary>The local model server failed or refused; the message is fit to show the owner.</summary>
public class LocalModelHostException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A local model server (Ollama) the owner already runs: found, asked what it has, and asked to
/// download a model. Installing the server itself is the owner's job, never PersonaOS's.
/// </summary>
public interface ILocalModelHost
{
    /// <summary>The server at <paramref name="address"/>, or null when nothing answers there.</summary>
    Task<LocalHostInfo?> DetectAsync(string address, CancellationToken ct = default);

    /// <summary>Whether the server already has <paramref name="model"/>.</summary>
    Task<bool> HasModelAsync(string address, string model, CancellationToken ct = default);

    /// <summary>Downloads <paramref name="model"/> into the server, reporting progress as it goes.</summary>
    IAsyncEnumerable<ModelPullProgress> PullAsync(string address, string model, CancellationToken ct = default);
}
