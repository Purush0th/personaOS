using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the PersonaOS install. SQL Server backed; implements the
/// Application persistence port. Migrations are auto-applied on API startup.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Reminder>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            cfg.Property(x => x.Status).HasMaxLength(20).IsRequired();
            cfg.Property(x => x.LastError).HasMaxLength(1000);
            // Deleting a goal / planner item keeps the reminder, just unlinked.
            cfg.HasOne(x => x.Goal).WithMany()
                .HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.SetNull);
            cfg.HasOne(x => x.PlannerItem).WithMany()
                .HasForeignKey(x => x.PlannerItemId).OnDelete(DeleteBehavior.SetNull);
            // The dispatcher's hot query: pending reminders that are now due.
            cfg.HasIndex(x => new { x.Status, x.DueAtUtc });
        });

        modelBuilder.Entity<DeviceToken>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Token).HasMaxLength(500).IsRequired();
            cfg.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            cfg.Property(x => x.DeviceName).HasMaxLength(200);
            cfg.HasIndex(x => x.Token).IsUnique();
        });

        modelBuilder.Entity<PlannerItem>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Title).HasMaxLength(300).IsRequired();
            cfg.Property(x => x.Notes).HasMaxLength(4000);
            cfg.Property(x => x.Status).HasMaxLength(20).IsRequired();
            // Deleting a goal keeps its planner items; they simply become unlinked.
            cfg.HasOne(x => x.Goal)
                .WithMany()
                .HasForeignKey(x => x.GoalId)
                .OnDelete(DeleteBehavior.SetNull);
            cfg.HasIndex(x => new { x.Date, x.SortOrder });
        });

        modelBuilder.Entity<InstanceConfig>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Id).ValueGeneratedNever();
            cfg.Property(x => x.AssistantNickname).HasMaxLength(100).IsRequired();
            cfg.Property(x => x.PersonaTemplate).HasMaxLength(4000);
            cfg.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
            cfg.Property(x => x.ClaudeModel).HasMaxLength(100).IsRequired();
            cfg.Property(x => x.AnthropicApiKeyEncrypted).HasMaxLength(2000);

            // Feature toggle map persisted as a JSON string column.
            var dictComparer = new ValueComparer<Dictionary<string, bool>>(
                (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
                v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
                v => JsonSerializer.Deserialize<Dictionary<string, bool>>(
                        JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? new());

            cfg.Property(x => x.Features)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOpts),
                    v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, JsonOpts) ?? new())
                .Metadata.SetValueComparer(dictComparer);
        });

        modelBuilder.Entity<AdminUser>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Username).HasMaxLength(100).IsRequired();
            cfg.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            cfg.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Conversation>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Title).HasMaxLength(200).IsRequired();
            cfg.HasMany(x => x.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            cfg.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<ChatMessage>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Role).HasMaxLength(20).IsRequired();
            cfg.Property(x => x.Content).IsRequired();
            cfg.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<Goal>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Title).HasMaxLength(300).IsRequired();
            cfg.Property(x => x.Description).HasMaxLength(4000);
            cfg.Property(x => x.PeriodType).HasMaxLength(20).IsRequired();
            cfg.Property(x => x.Status).HasMaxLength(20).IsRequired();
            // Self-referencing FK: SQL Server forbids cascade here; the subtree is
            // deleted explicitly in GoalService.DeleteAsync.
            cfg.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentGoalId)
                .OnDelete(DeleteBehavior.Restrict);
            cfg.HasIndex(x => x.ParentGoalId);
            cfg.HasIndex(x => new { x.Status, x.PeriodStart });
        });

        modelBuilder.Entity<UserProfile>(cfg =>
        {
            cfg.HasKey(x => x.Id);
            cfg.Property(x => x.Id).ValueGeneratedNever();
            cfg.Property(x => x.AboutMe).HasMaxLength(8000);
        });
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
