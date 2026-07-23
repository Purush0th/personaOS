using System.Text;
using Microsoft.Extensions.Configuration;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Storage;

/// <summary>
/// Stores document bytes on the local filesystem under the configured root
/// ("Documents:StoragePath", default ./data/docs-storage). On-disk names are
/// server-generated GUIDs, and every resolved path is verified to stay inside
/// the root, so a crafted name cannot escape the storage directory.
/// </summary>
public class FileSystemDocumentStorage : IDocumentStorage
{
    private readonly string _root;

    public FileSystemDocumentStorage(IConfiguration configuration)
    {
        var configured = configuration["Documents:StoragePath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "docs-storage")
            : configured);

        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        // The stored name never derives from user input.
        var safeExtension = SafeExtension(extension);
        var storageName = $"{Guid.NewGuid():N}{safeExtension}";

        await using var target = File.Create(ResolvePath(storageName));
        await content.CopyToAsync(target, ct);

        return storageName;
    }

    public Task<Stream?> OpenReadAsync(string storageName, CancellationToken ct = default)
    {
        var path = ResolvePath(storageName);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        return Task.FromResult(stream);
    }

    public async Task<string?> ReadTextAsync(string storageName, int maxCharacters, CancellationToken ct = default)
    {
        var path = ResolvePath(storageName);
        if (!File.Exists(path)) return null;

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // Read one character past the limit: getting it back means there was more
        // to read, which detects truncation without a blocking EndOfStream check.
        var buffer = new char[maxCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer, ct);

        return read > maxCharacters
            ? new string(buffer, 0, maxCharacters) + "\n\n[truncated — the document is longer than this excerpt]"
            : new string(buffer, 0, read);
    }

    public Task DeleteAsync(string storageName, CancellationToken ct = default)
    {
        var path = ResolvePath(storageName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Resolves a storage name to an absolute path, refusing anything outside the root.</summary>
    private string ResolvePath(string storageName)
    {
        if (string.IsNullOrWhiteSpace(storageName))
            throw new ArgumentException("Storage name is required.", nameof(storageName));

        var full = Path.GetFullPath(Path.Combine(_root, storageName));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Resolved document path escapes the storage root.");

        return full;
    }

    private static string SafeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;

        var trimmed = extension.Trim();
        if (!trimmed.StartsWith('.')) trimmed = "." + trimmed;

        // Reject anything that isn't a simple ".ext".
        return trimmed.Length <= 20 && trimmed[1..].All(char.IsLetterOrDigit)
            ? trimmed.ToLowerInvariant()
            : string.Empty;
    }
}
