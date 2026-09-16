using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.WorkItems;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Comments and attachments on tasks and goals. Both behave the same way, so one controller serves
/// both rather than duplicating the routes per module: <c>/api/items/task/TASK-7/comments</c>.
/// </summary>
[ApiController]
[Route("api/items")]
[Authorize]
public class WorkItemsController(IWorkItemService items) : ControllerBase
{
    public record CommentBody(string Body, string? Author = null);

    [HttpGet("{itemType}/{key}/comments")]
    public async Task<IActionResult> Comments(string itemType, string key, CancellationToken ct)
    {
        var item = await items.ResolveAsync(itemType, key, ct);
        return item is null ? NotFound() : Ok(await items.ListCommentsAsync(item, ct));
    }

    [HttpPost("{itemType}/{key}/comments")]
    public async Task<IActionResult> AddComment(
        string itemType, string key, [FromBody] CommentBody body, CancellationToken ct)
    {
        var item = await items.ResolveAsync(itemType, key, ct);
        if (item is null) return NotFound();
        return Ok(await items.AddCommentAsync(item, new AddCommentRequest(body.Body, body.Author), ct));
    }

    [HttpPut("comments/{id:int}")]
    public async Task<IActionResult> UpdateComment(int id, [FromBody] CommentBody body, CancellationToken ct)
    {
        var comment = await items.UpdateCommentAsync(id, body.Body, ct);
        return comment is null ? NotFound() : Ok(comment);
    }

    [HttpDelete("comments/{id:int}")]
    public async Task<IActionResult> DeleteComment(int id, CancellationToken ct) =>
        await items.DeleteCommentAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("{itemType}/{key}/attachments")]
    public async Task<IActionResult> Attachments(string itemType, string key, CancellationToken ct)
    {
        var item = await items.ResolveAsync(itemType, key, ct);
        return item is null ? NotFound() : Ok(await items.ListAttachmentsAsync(item, ct));
    }

    [HttpPost("{itemType}/{key}/attachments")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<IActionResult> AddAttachment(
        string itemType, string key, IFormFile file, CancellationToken ct)
    {
        var item = await items.ResolveAsync(itemType, key, ct);
        if (item is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { error = "A file is required." });

        await using var stream = file.OpenReadStream();
        var attachment = await items.AddAttachmentAsync(
            item, new AddAttachmentRequest(file.FileName, file.ContentType, stream), ct);
        return Ok(attachment);
    }

    [HttpGet("attachments/{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var content = await items.DownloadAttachmentAsync(id, ct);
        return content is null
            ? NotFound()
            : File(content.Content, content.ContentType, content.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("attachments/{id:int}")]
    public async Task<IActionResult> DeleteAttachment(int id, CancellationToken ct) =>
        await items.DeleteAttachmentAsync(id, ct) ? NoContent() : NotFound();
}
