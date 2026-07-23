using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Documents;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Docs)]
public class DocumentsController(IDocumentService documents) : ControllerBase
{
    public record UpdateDescriptionRequest(string? Description);

    /// <summary>Uploaded documents, newest first. Optional name/description search.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct) =>
        Ok(await documents.ListAsync(search, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var document = await documents.GetAsync(id, ct);
        return document is null ? NotFound() : Ok(document);
    }

    /// <summary>Downloads the original file.</summary>
    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var content = await documents.DownloadAsync(id, ct);
        return content is null
            ? NotFound()
            : File(content.Content, content.ContentType, content.FileName);
    }

    /// <summary>Uploads a file (multipart/form-data).</summary>
    [HttpPost]
    [RequestSizeLimit(26_214_400)] // 25 MB, matching the service-level cap
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string? description,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A file is required.", code = "document_validation_failed" });

        await using var stream = file.OpenReadStream();
        var document = await documents.UploadAsync(
            new UploadDocumentRequest(stream, file.FileName, file.ContentType, description), ct);

        return CreatedAtAction(nameof(Get), new { id = document.Id }, document);
    }

    [HttpPut("{id:int}/description")]
    public async Task<IActionResult> UpdateDescription(
        int id, [FromBody] UpdateDescriptionRequest request, CancellationToken ct)
    {
        var document = await documents.UpdateDescriptionAsync(id, request.Description, ct);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await documents.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
