using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Speech;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// The optional speech service, orchestrated by the API: the phone uploads a recording and gets
/// text back, or sends a reply and gets audio to play. It never talks to the speech server itself,
/// so the key stays here.
/// </summary>
[ApiController]
[Route("api/speech")]
[Authorize]
public class SpeechController(ISpeechService speech) : ControllerBase
{
    /// <summary>The largest recording accepted; a minute of compressed speech is well under a megabyte.</summary>
    private const long MaxAudioBytes = 25 * 1024 * 1024;

    public record SpeakRequest(string? Text);

    public record TranscriptResponse(string Text);

    public record TestResponse(bool Ok, string Message);

    /// <summary>Which directions a speech service is set up for; clients offer "Server" only for those.</summary>
    [HttpGet]
    public async Task<ActionResult<SpeechStatus>> Status(CancellationToken ct) => Ok(await speech.GetStatusAsync(ct));

    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SpeechStatus>> Update([FromBody] SpeechSettings settings, CancellationToken ct) =>
        Ok(await speech.UpdateAsync(settings, ct));

    /// <summary>Tries the service (saved, or the fields given) without saving. A failure is a 200 with Ok=false.</summary>
    [HttpPost("test")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<TestResponse>> Test([FromBody] SpeechSettings? settings, CancellationToken ct)
    {
        var (ok, message) = await speech.TestAsync(settings, ct);
        return Ok(new TestResponse(ok, message));
    }

    [HttpPost("transcribe")]
    [RequireFeature(InstanceConfig.Modules.Voice)]
    [RequestSizeLimit(MaxAudioBytes)]
    public async Task<ActionResult<TranscriptResponse>> Transcribe(IFormFile? audio, [FromForm] string? language, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0) return BadRequest(new { error = "Send the recording as the 'audio' file." });
        if (audio.Length > MaxAudioBytes) return BadRequest(new { error = "The recording is too long." });

        return await Run(async () =>
        {
            await using var stream = audio.OpenReadStream();
            var text = await speech.TranscribeAsync(stream, string.IsNullOrWhiteSpace(audio.FileName) ? "speech.m4a" : audio.FileName, language, ct);
            return Ok(new TranscriptResponse(text));
        });
    }

    [HttpPost("speak")]
    [RequireFeature(InstanceConfig.Modules.Voice)]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken ct) =>
        await Run(async () =>
        {
            var audio = await speech.SpeakAsync(request.Text ?? string.Empty, ct);
            return File(audio.Bytes, audio.ContentType);
        });

    /// <summary>Not set up is a 409 the phone falls back on; a failing service is a 502 with its reason.</summary>
    private async Task<ActionResult> Run(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SpeechNotConfiguredException ex)
        {
            return Conflict(new { error = ex.Message, code = "speech_not_configured" });
        }
        catch (SpeechServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message, code = "speech_failed" });
        }
    }
}
