using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Auth;

/// <summary>JWT implementation of the token-creation port.</summary>
public class JwtTokenGenerator(IOptions<JwtOptions> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public TokenResult CreateToken(AdminUser admin)
    {
        if (string.IsNullOrWhiteSpace(_jwt.SigningKey))
            throw new InvalidOperationException("Missing configuration 'Jwt:SigningKey'.");

        var expires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, admin.Username),
                new Claim(ClaimTypes.Role, "admin"),
            ]),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new TokenResult(token, expires);
    }
}
