using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Documents;

public record DocumentDto(
    int Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Description,
    DateTime CreatedAtUtc);

/// <summary>A document's bytes plus the metadata needed to serve them.</summary>
public record DocumentContent(Stream Content, string FileName, string ContentType);

public record UploadDocumentRequest(
    Stream Content,
    string FileName,
    string? ContentType = null,
    string? Description = null);

/// <summary>Invalid input to a document operation; message is user/model-presentable.</summary>
public class DocumentValidationException(string message)
    : DomainValidationException(message, "document_validation_failed");

public interface IDocumentService
{
    /// <summary>All stored documents, newest first. Optional name/description search.</summary>
    Task<IReadOnlyList<DocumentDto>> ListAsync(string? search = null, CancellationToken ct = default);

    Task<DocumentDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Opens a document for download, or null when it does not exist.</summary>
    Task<DocumentContent?> DownloadAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns a document's text for the assistant to read. Non-text formats
    /// (PDF, images, Office files) report that they cannot be read as text —
    /// extraction is deliberately out of scope for v1 (no RAG).
    /// </summary>
    Task<string> ReadAsTextAsync(int id, CancellationToken ct = default);

    Task<DocumentDto> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default);

    Task<DocumentDto?> UpdateDescriptionAsync(int id, string? description, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
