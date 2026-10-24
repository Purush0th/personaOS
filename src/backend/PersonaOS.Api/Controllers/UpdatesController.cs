using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Updates;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Settings → Updates: the running version, the newest release, Update now, and whether new
/// releases install by themselves. Installing goes through the updater container.
/// </summary>
[ApiController]
[Route("api/updates")]
[Authorize]
public class UpdatesController(IUpdateService updates) : ControllerBase
{
    public record InstallRequest(string? Version);

    public record AutoInstallRequest(bool Enabled);

    [HttpGet]
    public async Task<ActionResult<UpdatesView>> Get(CancellationToken ct) => Ok(await updates.GetAsync(ct));

    [HttpPost("install")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdatesView>> Install([FromBody] InstallRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await updates.InstallAsync(request.Version ?? string.Empty, ct));
        }
        catch (UpdateRefusedException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("auto-install")]
    public async Task<ActionResult<UpdatesView>> SetAutoInstall([FromBody] AutoInstallRequest request, CancellationToken ct) =>
        Ok(await updates.SetAutoInstallAsync(request.Enabled, ct));
}
