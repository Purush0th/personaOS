namespace PersonaOS.Domain.Entities;

/// <summary>
/// Metadata for a user-uploaded file. Bytes live on the filesystem under the
/// install's docs-storage root; <see cref="StorageName"/> is a server-generated
/// name so nothing user-supplied ever reaches a filesystem path.
/// </summary>
public class Document
{
    public int Id { get; set; }

    /// <summary>Display name (the user's original file name, sanitized).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Server-generated on-disk name. Never user-controlled.</summary>
    public string StorageName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>Optional user/assistant-supplied note about what this document is.</summary>
    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
