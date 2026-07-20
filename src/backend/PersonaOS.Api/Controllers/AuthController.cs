using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Auth;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    public record LoginRequest(string Username, string Password);
    public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string Username);

    /// <summary>Exchange admin username/password for a JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required." });

        var result = await authService.LoginAsync(request.Username, request.Password, ct);
        if (result is null)
            return Unauthorized(new { error = "Invalid username or password." });

        return Ok(new LoginResponse(result.AccessToken, result.ExpiresAtUtc, result.Username));
    }
}
