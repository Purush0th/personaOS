using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Unauthenticated, safe-subset endpoint the mobile app + dashboard call at load
/// to display the assistant nickname, gate module UI by enabled features, and check
/// client-version compatibility. Never returns secrets (API key, password hashes).
/// </summary>
[ApiController]
[Route("api/branding")]
public class BrandingController(IInstanceConfigService configService) : ControllerBase
{
    public record BrandingResponse(
        string AssistantNickname,
        bool IsConfigured,
        IReadOnlyList<string> EnabledFeatures,
        string ApiVersion,
        string MinSupportedClient);

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingResponse>> Get(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);

        var enabled = config.Features
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .OrderBy(k => k)
            .ToList();

        return Ok(new BrandingResponse(
            AssistantNickname: config.AssistantNickname,
            IsConfigured: config.IsConfigured,
            EnabledFeatures: enabled,
            ApiVersion: ApiVersionInfo.ApiVersion,
            MinSupportedClient: ApiVersionInfo.MinSupportedClient));
    }
}
