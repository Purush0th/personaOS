using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Documents;

public class DocumentService(
    IAppDbContext db,
    IDocumentStorage storage) : IDocumentService
{
    /// <summary>Upper bound on a single upload (25 MB).</summary>
    private const long MaxUploadBytes = 25L * 1024 * 1024;

    /// <summary>How much text a single tool read returns before truncating.</summary>
    private const int MaxToolReadCharacters = 20_000;

    /// <summary>Extensions we will hand to the model as text.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".log", ".html", ".htm", ".css", ".js", ".ts", ".cs", ".py", ".sql", ".sh", ".ini", ".cfg",
    };

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var query = db.Documents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(d =>
                EF.Functions.Like(d.FileName, $"%{term}%") ||
                (d.Description != null && EF.Functions.Like(d.Description, $"%{term}%")));
        }

        return await query
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Select(d => new DocumentDto(
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Description, d.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<DocumentDto?> GetAsync(int id, CancellationToken ct = default) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentDto(
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Description, d.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);

    public async Task<DocumentContent?> DownloadAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        var stream = await storage.OpenReadAsync(doc.StorageName, ct);
        return stream is null ? null : new DocumentContent(stream, doc.FileName, doc.ContentType);
    }

    public async Task<string> ReadAsTextAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DocumentValidationException($"Document {id} does not exist.");

        var extension = Path.GetExtension(doc.FileName);
        if (!TextExtensions.Contains(extension))
            throw new DocumentValidationException(
                $"'{doc.FileName}' is a {extension.TrimStart('.').ToUpperInvariant()} file, which cannot be read as text. " +
                "Only plain-text formats (txt, md, csv, json, xml, code files) can be read.");

        var text = await storage.ReadTextAsync(doc.StorageName, MaxToolReadCharacters, ct)
            ?? throw new DocumentValidationException(
                $"The stored file for '{doc.FileName}' is missing.");

        return text;
    }

    public async Task<DocumentDto> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default)
    {
        var fileName = SanitizeFileName(request.FileName);
        var extension = Path.GetExtension(fileName);

        // Buffer through storage, then verify the persisted size against the cap.
        var storageName = await storage.SaveAsync(request.Content, extension, ct);

        long size;
        await using (var written = await storage.OpenReadAsync(storageName, ct))
        {
            size = written?.Length ?? 0;
        }

        if (size > MaxUploadBytes)
        {
            await storage.DeleteAsync(storageName, ct);
            throw new DocumentValidationException(
                $"File is larger than the {MaxUploadBytes / (1024 * 1024)} MB limit.");
        }

        if (size == 0)
        {
            await storage.DeleteAsync(storageName, ct);
            throw new DocumentValidationException("File is empty.");
        }

        var document = new Document
        {
            FileName = fileName,
            StorageName = storageName,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType,
            SizeBytes = size,
            Description = request.Description?.Trim(),
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return new DocumentDto(
            document.Id, document.FileName, document.ContentType,
            document.SizeBytes, document.Description, document.CreatedAtUtc);
    }

    public async Task<DocumentDto?> UpdateDescriptionAsync(int id, string? description, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return null;

        doc.Description = description?.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;

        db.Documents.Remove(doc);
        await db.SaveChangesAsync(ct);
        // Remove bytes only after the row is gone, so a failure here leaves an
        // orphaned file rather than a row pointing at nothing.
        await storage.DeleteAsync(doc.StorageName, ct);
        return true;
    }

    /// <summary>
    /// Reduces a user-supplied name to a bare file name. Defence in depth: the
    /// on-disk name is server-generated anyway, so this only guards the display value.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(name))
            throw new DocumentValidationException("A file name is required.");

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length > 255 ? name[^255..] : name;
    }
}
