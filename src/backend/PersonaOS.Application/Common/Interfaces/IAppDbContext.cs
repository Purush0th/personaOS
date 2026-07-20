using Microsoft.EntityFrameworkCore;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Common.Interfaces;

/// <summary>
/// Persistence port. Application code queries via LINQ over these sets;
/// the EF Core implementation lives in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<InstanceConfig> InstanceConfig { get; }
    DbSet<AdminUser> AdminUsers { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<UserProfile> UserProfile { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
