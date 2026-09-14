using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Push;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Bring-your-own Firebase push: the admin uploads their own project's files here.
///
/// The service-account key is write-only. It is encrypted on the way in and never returned by
/// any endpoint — status reports only whether push is on and which project it points at.
/// </summary>
[ApiController]
[Route("api/setup/push")]
[Authorize(Roles = "admin")]
public class PushConfigController(IPushConfigService push) : ControllerBase
{
    /// <summary>The two Firebase files, sent as text. The web page reads each file and posts its contents.</summary>
    public record SetPushConfigRequest(string? ServiceAccountJson, string? GoogleServicesJson);

    [HttpGet]
    public async Task<ActionResult<PushConfigStatus>> Status(CancellationToken ct) =>
        Ok(await push.GetStatusAsync(ct));

    /// <summary>Validates both files and switches push on. Rejects the pair if they disagree.</summary>
    [HttpPut]
    [RequestSizeLimit(128_000)] // Both files are a few kilobytes; nothing legitimate is larger.
    public async Task<ActionResult<PushConfigStatus>> Set([FromBody] SetPushConfigRequest request, CancellationToken ct) =>
        Ok(await push.SetAsync(request.ServiceAccountJson ?? string.Empty, request.GoogleServicesJson ?? string.Empty, ct));

    /// <summary>Deletes the stored credential and turns push off.</summary>
    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await push.ClearAsync(ct);
        return NoContent();
    }
}
