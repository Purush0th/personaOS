using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Profile;

/// <summary>What the assistant knows about the user, as they wrote it.</summary>
public record UserProfileDto(string AboutMe, DateTime UpdatedAtUtc);

public class ProfileValidationException(string message) : DomainValidationException(message, "profile_invalid");

public interface IUserProfileService
{
    Task<UserProfileDto> GetAsync(CancellationToken ct = default);

    /// <summary>Replaces the text. Blank clears it.</summary>
    Task<UserProfileDto> UpdateAsync(string aboutMe, CancellationToken ct = default);

    /// <summary>Adds one fact on its own line, unless the text already says it.</summary>
    Task<UserProfileDto> RememberAsync(string fact, CancellationToken ct = default);
}

/// <summary>
/// The single user's "about me": free-form facts and preferences ("vegetarian, lives in Chennai,
/// prefers short answers") that go into every system prompt, so the assistant is theirs.
/// </summary>
public class UserProfileService(IAppDbContext db) : IUserProfileService
{
    /// <summary>Every character is sent with every message, so the text is kept to a paragraph or two.</summary>
    public const int MaxLength = 2_000;

    public async Task<UserProfileDto> GetAsync(CancellationToken ct = default)
    {
        var profile = await db.UserProfile.AsNoTracking().FirstOrDefaultAsync(ct);
        return profile is null ? new UserProfileDto(string.Empty, DateTime.MinValue) : ToDto(profile);
    }

    public async Task<UserProfileDto> UpdateAsync(string aboutMe, CancellationToken ct = default)
    {
        var text = (aboutMe ?? string.Empty).Trim();
        if (text.Length > MaxLength)
            throw new ProfileValidationException($"Keep it under {MaxLength} characters; it is sent with every message.");

        var profile = await LoadAsync(ct);
        profile.AboutMe = text;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(profile);
    }

    public async Task<UserProfileDto> RememberAsync(string fact, CancellationToken ct = default)
    {
        var line = (fact ?? string.Empty).Trim();
        if (line.Length == 0) throw new ProfileValidationException("There is nothing to remember.");

        var current = (await GetAsync(ct)).AboutMe;
        if (current.Contains(line, StringComparison.OrdinalIgnoreCase)) return await GetAsync(ct);

        return await UpdateAsync(current.Length == 0 ? line : $"{current}\n{line}", ct);
    }

    private async Task<UserProfile> LoadAsync(CancellationToken ct)
    {
        var profile = await db.UserProfile.FirstOrDefaultAsync(ct);
        if (profile is not null) return profile;

        profile = new UserProfile();
        db.UserProfile.Add(profile);
        return profile;
    }

    private static UserProfileDto ToDto(UserProfile profile) => new(profile.AboutMe, profile.UpdatedAtUtc);
}
