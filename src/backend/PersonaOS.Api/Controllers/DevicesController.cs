using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Push;
using PersonaOS.Application.Reminders;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Push-token registration for the signed-in user's devices.
///
/// Deliberately NOT behind a feature toggle. These routes used to live on the reminders
/// controller and inherited its <c>RequireFeature(reminders)</c>, so switching reminders off
/// also stopped a phone registering at all — and proactive briefs push to the same tokens, so
/// they went silent too, with nothing to say why. A device token is shared delivery plumbing,
/// not part of any one module; each module decides for itself whether to send.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController(IReminderService reminders, IPushConfigService push) : ControllerBase
{
    /// <summary>
    /// The Firebase client options the app starts Firebase with, or 204 when push is not set up.
    ///
    /// This is what keeps Firebase config out of the release APK: the app has no project baked
    /// in and asks its own server after sign-in. A reinstalled app therefore recovers push the
    /// moment it signs in, with nothing to re-enter on the phone. The values are identifiers,
    /// not secrets — the service-account key is never exposed here or anywhere else.
    /// </summary>
    [HttpGet("push-config")]
    public async Task<IActionResult> PushConfig(CancellationToken ct)
    {
        var options = await push.GetClientOptionsAsync(ct);
        return options is null ? NoContent() : Ok(options);
    }

    /// <summary>Registers or refreshes this device's push token. Idempotent per token.</summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        await reminders.RegisterDeviceAsync(request, ct);
        return NoContent();
    }

    /// <summary>Removes a device token (sign-out, or a token the app has rotated away from).</summary>
    [HttpDelete("{token}")]
    public async Task<IActionResult> Unregister(string token, CancellationToken ct) =>
        await reminders.UnregisterDeviceAsync(token, ct) ? NoContent() : NotFound();
}
