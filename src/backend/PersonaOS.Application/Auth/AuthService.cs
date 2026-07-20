using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Auth;

public class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator) : IAuthService
{
    public async Task<bool> AdminExistsAsync(CancellationToken ct = default) =>
        await db.AdminUsers.AnyAsync(ct);

    public async Task CreateAdminAsync(string username, string password, CancellationToken ct = default)
    {
        if (await AdminExistsAsync(ct))
            throw new InvalidOperationException("An admin account already exists.");

        db.AdminUsers.Add(new AdminUser
        {
            Username = username.Trim(),
            PasswordHash = passwordHasher.Hash(password),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthResult?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);
        if (admin is null)
            return null;

        var verify = passwordHasher.Verify(admin.PasswordHash, password);
        if (verify == PasswordVerifyResult.Failed)
            return null;

        if (verify == PasswordVerifyResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = passwordHasher.Hash(password);
        }

        admin.LastLoginUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var token = tokenGenerator.CreateToken(admin);
        return new AuthResult(token.AccessToken, token.ExpiresAtUtc, admin.Username);
    }
}
