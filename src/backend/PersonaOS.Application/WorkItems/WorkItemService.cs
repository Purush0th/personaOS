using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.WorkItems;

public class WorkItemService(IAppDbContext db, IDocumentStorage storage) : IWorkItemService
{
    /// <summary>Upper bound on a single attachment (25 MB), matching documents.</summary>
    private const long MaxAttachmentBytes = 25L * 1024 * 1024;

    private const int MaxCommentLength = 8000;

    public async Task<WorkItemRef?> ResolveAsync(string itemType, string key, CancellationToken ct = default)
    {
        var type = (itemType ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkItemTypes.All.Contains(type))
            throw new WorkItemValidationException($"Item type must be one of: {string.Join(", ", WorkItemTypes.All)}.");

        if (type == WorkItemTypes.Task)
        {
            var number = ItemKeys.Parse(key, ItemKeys.TaskPrefix);
            var task = number is null
                ? null
                : await db.BoardTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Number == number, ct);
            return task is null ? null : new WorkItemRef(type, task.Id, ItemKeys.Task(task.Number), task.Title);
        }

        var goalNumber = ItemKeys.Parse(key, ItemKeys.GoalPrefix);
        var goal = goalNumber is null
            ? null
            : await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Number == goalNumber, ct);
        return goal is null ? null : new WorkItemRef(type, goal.Id, ItemKeys.Goal(goal.Number), goal.Title);
    }

    public async Task<IReadOnlyList<WorkItemCommentDto>> ListCommentsAsync(
        WorkItemRef item, CancellationToken ct = default) =>
        await db.WorkItemComments.AsNoTracking()
            .Where(c => c.ItemType == item.ItemType && c.ItemId == item.ItemId)
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Select(c => new WorkItemCommentDto(c.Id, c.Author, c.Body, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(ct);

    public async Task<WorkItemCommentDto> AddCommentAsync(
        WorkItemRef item, AddCommentRequest request, CancellationToken ct = default)
    {
        var body = RequireBody(request.Body);
        var author = (request.Author ?? CommentAuthors.User).Trim().ToLowerInvariant();
        if (!CommentAuthors.All.Contains(author))
            throw new WorkItemValidationException($"Author must be one of: {string.Join(", ", CommentAuthors.All)}.");

        var comment = new WorkItemComment
        {
            ItemType = item.ItemType,
            ItemId = item.ItemId,
            Author = author,
            Body = body,
        };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return new WorkItemCommentDto(comment.Id, comment.Author, comment.Body, comment.CreatedAtUtc, comment.UpdatedAtUtc);
    }

    public async Task<WorkItemCommentDto?> UpdateCommentAsync(int id, string body, CancellationToken ct = default)
    {
        var comment = await db.WorkItemComments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return null;

        comment.Body = RequireBody(body);
        comment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new WorkItemCommentDto(comment.Id, comment.Author, comment.Body, comment.CreatedAtUtc, comment.UpdatedAtUtc);
    }

    public async Task<bool> DeleteCommentAsync(int id, CancellationToken ct = default)
    {
        var comment = await db.WorkItemComments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return false;

        db.WorkItemComments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<WorkItemAttachmentDto>> ListAttachmentsAsync(
        WorkItemRef item, CancellationToken ct = default) =>
        await db.WorkItemAttachments.AsNoTracking()
            .Where(a => a.ItemType == item.ItemType && a.ItemId == item.ItemId)
            .OrderBy(a => a.Id)
            .Select(a => new WorkItemAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<WorkItemAttachmentDto> AddAttachmentAsync(
        WorkItemRef item, AddAttachmentRequest request, CancellationToken ct = default)
    {
        var fileName = SanitizeFileName(request.FileName);
        var storageName = await storage.SaveAsync(request.Content, Path.GetExtension(fileName), ct);

        // Buffer through storage, then verify the persisted size against the cap.
        long size;
        await using (var written = await storage.OpenReadAsync(storageName, ct))
        {
            size = written?.Length ?? 0;
        }

        if (size == 0 || size > MaxAttachmentBytes)
        {
            await storage.DeleteAsync(storageName, ct);
            throw new WorkItemValidationException(size == 0
                ? "That file is empty."
                : $"That file is larger than the {MaxAttachmentBytes / (1024 * 1024)} MB limit.");
        }

        var attachment = new WorkItemAttachment
        {
            ItemType = item.ItemType,
            ItemId = item.ItemId,
            FileName = fileName,
            StorageName = storageName,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType,
            SizeBytes = size,
        };
        db.WorkItemAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new WorkItemAttachmentDto(
            attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.CreatedAtUtc);
    }

    public async Task<AttachmentContent?> DownloadAttachmentAsync(int id, CancellationToken ct = default)
    {
        var attachment = await db.WorkItemAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null) return null;

        var stream = await storage.OpenReadAsync(attachment.StorageName, ct);
        return stream is null ? null : new AttachmentContent(stream, attachment.FileName, attachment.ContentType);
    }

    public async Task<bool> DeleteAttachmentAsync(int id, CancellationToken ct = default)
    {
        var attachment = await db.WorkItemAttachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null) return false;

        db.WorkItemAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        // Remove bytes only after the row is gone, so a failure here leaves an orphaned file
        // rather than a row pointing at nothing.
        await storage.DeleteAsync(attachment.StorageName, ct);
        return true;
    }

    private static string RequireBody(string? body)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new WorkItemValidationException("A comment cannot be empty.");
        return trimmed.Length > MaxCommentLength ? trimmed[..MaxCommentLength] : trimmed;
    }

    /// <summary>
    /// Reduces a user-supplied name to a bare file name. Defence in depth: the on-disk name is
    /// server-generated anyway, so this only guards the display value.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(name)) throw new WorkItemValidationException("A file name is required.");

        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name.Length > 255 ? name[^255..] : name;
    }
}
