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
        // Match the username case-insensitively and explicitly, rather than leaning on the
        // provider's default collation: SQL Server's default is case-insensitive but SQLite's
        // is BINARY, so a plain `==` silently changed login behaviour when the store moved.
        // The column is also declared COLLATE NOCASE, which keeps the unique index in step.
        var normalized = username.Trim().ToLowerInvariant();
        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized, ct);
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
