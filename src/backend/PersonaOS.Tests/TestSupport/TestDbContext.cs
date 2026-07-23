using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.TestSupport;

/// <summary>
/// In-memory implementation of the persistence port. Tests exercise Application
/// services without touching Infrastructure or a real database.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<InstanceConfig> InstanceConfig => Set<InstanceConfig>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<UserProfile> UserProfile => Set<UserProfile>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<PlannerItem> PlannerItems => Set<PlannerItem>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ProactiveJobRun> ProactiveJobRuns => Set<ProactiveJobRun>();

    public override Task<int> SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mirror the production JSON mapping for the feature-toggle map.
        modelBuilder.Entity<InstanceConfig>()
            .Property(x => x.Features)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null) ?? new());
    }

    /// <summary>A fresh, isolated database per test.</summary>
    public static TestDbContext Create() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"personaos-tests-{Guid.NewGuid():N}")
            .Options);
}
