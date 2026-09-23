using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Profile;

namespace PersonaOS.Api.Controllers;

/// <summary>What the assistant knows about the user: read and edited on the Settings page.</summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(IUserProfileService profile) : ControllerBase
{
    public record UpdateProfileRequest(string? AboutMe);

    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> Get(CancellationToken ct) => Ok(await profile.GetAsync(ct));

    /// <summary>Replaces the text; an empty one clears it.</summary>
    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> Update([FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await profile.UpdateAsync(request.AboutMe ?? string.Empty, ct));
}
