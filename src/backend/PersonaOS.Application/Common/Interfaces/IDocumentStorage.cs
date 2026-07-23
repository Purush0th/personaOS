namespace PersonaOS.Application.Common.Interfaces;

/// <summary>
/// Blob storage port for uploaded documents. Implementations own where bytes
/// physically live; Application only deals in server-generated storage names.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Writes the stream and returns the generated storage name.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>Opens a stored blob for reading, or null when it is missing.</summary>
    Task<Stream?> OpenReadAsync(string storageName, CancellationToken ct = default);

    /// <summary>Reads a stored blob as text, or null when missing.</summary>
    Task<string?> ReadTextAsync(string storageName, int maxCharacters, CancellationToken ct = default);

    /// <summary>Deletes a stored blob. No-op when already gone.</summary>
    Task DeleteAsync(string storageName, CancellationToken ct = default);
}
