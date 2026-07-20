using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Domain.Entities;
using PersonaOS.Application.Auth;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// First-run Setup Wizard endpoints. POST /api/setup works exactly once — while
/// the instance is unconfigured — and seeds the admin account + InstanceConfig.
/// Afterwards, edits go through PUT /api/setup behind admin auth.
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(
    IInstanceConfigService configService,
    IAuthService authService) : ControllerBase
{
    public record SetupStatusResponse(bool IsConfigured);

    public record SetupRequest(
        string AssistantNickname,
        string? PersonaTemplate,
        string AdminUsername,
        string AdminPassword,
        string AnthropicApiKey,
        string? ClaudeModel,
        string? TimeZone,
        Dictionary<string, bool>? Features);

    public record UpdateSettingsRequest(
        string? AssistantNickname,
        string? PersonaTemplate,
        string? AnthropicApiKey,
        string? ClaudeModel,
        string? TimeZone,
        Dictionary<string, bool>? Features);

    /// <summary>Whether this instance has completed first-run setup.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponse>> Status(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return Ok(new SetupStatusResponse(config.IsConfigured));
    }

    /// <summary>One-shot first-run setup. Rejected once the instance is configured.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Configure([FromBody] SetupRequest request, CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (config.IsConfigured || await authService.AdminExistsAsync(ct))
            return Conflict(new { error = "This instance is already configured. Use PUT /api/setup (authenticated) to change settings." });

        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        if (!TryResolveTimeZone(request.TimeZone, out var timeZone))
            return BadRequest(new { error = $"Unknown time zone '{request.TimeZone}'." });

        await authService.CreateAdminAsync(request.AdminUsername, request.AdminPassword, ct);
        await configService.SetAnthropicApiKeyAsync(request.AnthropicApiKey, ct);
        await configService.UpdateAsync(c =>
        {
            c.AssistantNickname = request.AssistantNickname.Trim();
            c.PersonaTemplate = request.PersonaTemplate?.Trim() ?? string.Empty;
            c.TimeZone = timeZone;
            if (!string.IsNullOrWhiteSpace(request.ClaudeModel))
                c.ClaudeModel = request.ClaudeModel.Trim();
            if (request.Features is not null)
                ApplyFeatureToggles(c, request.Features);
            c.IsConfigured = true;
        }, ct);

        return Ok(new { message = $"Setup complete. Say hello to {request.AssistantNickname.Trim()}!" });
    }

    /// <summary>Edit settings after setup. Admin JWT required. Only provided fields change.</summary>
    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        if (request.AnthropicApiKey is not null)
        {
            if (string.IsNullOrWhiteSpace(request.AnthropicApiKey))
                return BadRequest(new { error = "AnthropicApiKey must not be blank when provided." });
            await configService.SetAnthropicApiKeyAsync(request.AnthropicApiKey, ct);
        }

        string? resolvedTimeZone = null;
        if (request.TimeZone is not null && !TryResolveTimeZone(request.TimeZone, out resolvedTimeZone!))
            return BadRequest(new { error = $"Unknown time zone '{request.TimeZone}'." });

        await configService.UpdateAsync(c =>
        {
            if (!string.IsNullOrWhiteSpace(request.AssistantNickname))
                c.AssistantNickname = request.AssistantNickname.Trim();
            if (request.PersonaTemplate is not null)
                c.PersonaTemplate = request.PersonaTemplate.Trim();
            if (!string.IsNullOrWhiteSpace(request.ClaudeModel))
                c.ClaudeModel = request.ClaudeModel.Trim();
            if (resolvedTimeZone is not null)
                c.TimeZone = resolvedTimeZone;
            if (request.Features is not null)
                ApplyFeatureToggles(c, request.Features);
        }, ct);

        return Ok(new { message = "Settings updated." });
    }

    private static string? Validate(SetupRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.AssistantNickname)) return "AssistantNickname is required.";
        if (r.AssistantNickname.Trim().Length > 100) return "AssistantNickname must be 100 characters or fewer.";
        if (string.IsNullOrWhiteSpace(r.AdminUsername)) return "AdminUsername is required.";
        if (string.IsNullOrWhiteSpace(r.AdminPassword)) return "AdminPassword is required.";
        if (r.AdminPassword.Length < 8) return "AdminPassword must be at least 8 characters.";
        if (string.IsNullOrWhiteSpace(r.AnthropicApiKey)) return "AnthropicApiKey is required.";
        return null;
    }

    /// <summary>Only known module names can be toggled; unknown keys are ignored.</summary>
    private static void ApplyFeatureToggles(InstanceConfig config, Dictionary<string, bool> requested)
    {
        var known = InstanceConfig.DefaultFeatures().Keys;
        foreach (var key in known)
        {
            if (requested.TryGetValue(key, out var enabled))
                config.Features[key] = enabled;
        }
    }

    private static bool TryResolveTimeZone(string? id, out string resolved)
    {
        resolved = "UTC";
        if (string.IsNullOrWhiteSpace(id))
            return true; // default UTC

        try
        {
            resolved = TimeZoneInfo.FindSystemTimeZoneById(id.Trim()).Id;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
