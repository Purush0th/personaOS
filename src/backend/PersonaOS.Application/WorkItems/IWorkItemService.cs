using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.WorkItems;

/// <summary>Where a comment or attachment lives, resolved from a key like TASK-7 or GOAL-3.</summary>
public record WorkItemRef(string ItemType, int ItemId, string Key, string Title);

public record AddCommentRequest(string Body, string? Author = null);

public record AddAttachmentRequest(string FileName, string ContentType, Stream Content);

/// <summary>A downloadable attachment.</summary>
public record AttachmentContent(Stream Content, string FileName, string ContentType);

/// <summary>Invalid input to a comment or attachment operation; message is user-presentable.</summary>
public class WorkItemValidationException(string message)
    : DomainValidationException(message, "work_item_validation_failed");

/// <summary>
/// Comments and attachments on tasks and goals. Both kinds of item behave the same way here, so
/// the rules (and the storage) live in one place rather than once per module.
/// </summary>
public interface IWorkItemService
{
    /// <summary>Resolves "task"/"goal" plus a key, or null when either does not exist.</summary>
    Task<WorkItemRef?> ResolveAsync(string itemType, string key, CancellationToken ct = default);

    Task<IReadOnlyList<WorkItemCommentDto>> ListCommentsAsync(WorkItemRef item, CancellationToken ct = default);

    Task<WorkItemCommentDto> AddCommentAsync(
        WorkItemRef item, AddCommentRequest request, CancellationToken ct = default);

    Task<WorkItemCommentDto?> UpdateCommentAsync(int id, string body, CancellationToken ct = default);

    Task<bool> DeleteCommentAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<WorkItemAttachmentDto>> ListAttachmentsAsync(WorkItemRef item, CancellationToken ct = default);

    Task<WorkItemAttachmentDto> AddAttachmentAsync(
        WorkItemRef item, AddAttachmentRequest request, CancellationToken ct = default);

    Task<AttachmentContent?> DownloadAttachmentAsync(int id, CancellationToken ct = default);

    Task<bool> DeleteAttachmentAsync(int id, CancellationToken ct = default);
}
