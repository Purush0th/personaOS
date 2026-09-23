using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Infrastructure.Ai;
using PersonaOS.Infrastructure.Auth;
using PersonaOS.Infrastructure.Persistence;
using PersonaOS.Infrastructure.Push;
using PersonaOS.Infrastructure.Security;
using PersonaOS.Infrastructure.Storage;

namespace PersonaOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Binds every Application port to its Infrastructure adapter: EF Core (SQLite)
    /// persistence, Data Protection secrets, JWT creation, password hashing, and the AI
    /// provider adapters. The database file path is read from "Database:Path"
    /// (default "data/personaos.db"); an explicit "ConnectionStrings:PersonaOS" overrides it.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PersonaOS");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var dbPath = configuration["Database:Path"];
            if (string.IsNullOrWhiteSpace(dbPath))
                dbPath = Path.Combine("data", "personaos.db");

            // Ensure the parent directory exists — SQLite won't create it.
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                ForeignKeys = true, // SQLite enforces FKs only when asked; do it per connection.
            }.ToString();
        }

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        // Explicit factories: the protector has a purpose-taking constructor too, and resolving
        // it by convention would leave which purpose you get to constructor-selection rules.
        services.AddSingleton<ISecretProtector>(sp =>
            new DataProtectionSecretProtector(sp.GetRequiredService<IDataProtectionProvider>()));
        services.AddKeyedSingleton<ISecretProtector>(SecretPurposes.PushCredentials, (sp, _) =>
            new DataProtectionSecretProtector(
                sp.GetRequiredService<IDataProtectionProvider>(), SecretPurposes.PushCredentials));
        // AI providers: concrete adapters + a factory that selects one per InstanceConfig.
        services.AddSingleton<AnthropicMessageStreamer>();
        services.AddSingleton<OpenAiCompatibleMessageStreamer>();
        // A streamed reply from a local model can take minutes; the chat loop's idle timeout and
        // the request's cancellation bound it instead of HttpClient's 100-second default.
        services.AddSingleton(_ => new OllamaMessageStreamer(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }));
        services.AddSingleton<IAiMessageStreamerFactory, AiMessageStreamerFactory>();
        // Prompt fragments an installation has edited; everything else is the shipped default.
        services.AddSingleton<IPromptOverrideSource, FilePromptOverrideSource>();
        // Push: bring-your-own Firebase, configured from the admin page at runtime. One sender
        // instance serves both roles — the app sends through it, and uploading a key reconfigures
        // it — so the two can never disagree about whether push is on. Until a key is uploaded it
        // reports "not configured" and due reminders wait as pending.
        services.AddSingleton<ConfigurablePushSender>();
        services.AddSingleton<IPushSender>(sp => sp.GetRequiredService<ConfigurablePushSender>());
        services.AddSingleton<IPushProviderConfigurator>(sp => sp.GetRequiredService<ConfigurablePushSender>());
        services.AddHostedService<PushConfigLoader>();
        services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        // Nightly self-contained snapshot (db + docs + keyring). See BackupService.
        services.AddHostedService<BackupService>();

        return services;
    }

    /// <summary>
    /// Repairs assistant messages stored by a build that let an unclosed ``` fence through.
    /// Scrubbing happens on write, so replies saved before that fix keep the stray marker
    /// sitting in the middle of the text forever — a self-hoster upgrading should not have to
    /// live with it.
    ///
    /// Passed no tool names deliberately: that limits the scrubber to fence repair, so this can
    /// never retroactively strip JSON out of an old reply that legitimately contained some.
    /// Cheap (only rows containing a fence are considered) and idempotent — scrubbing clean
    /// text is a no-op, so running it on every boot is harmless.
    /// </summary>
    private static async Task RepairLeakedFencesAsync(AppDbContext db, CancellationToken ct)
    {
        var candidates = await db.ChatMessages
            .Where(m => m.Role == ChatRoles.Assistant && m.Content.Contains("```"))
            .ToListAsync(ct);

        var repaired = 0;
        foreach (var message in candidates)
        {
            var cleaned = LeakedToolCallScrubber.Scrub(message.Content, []);
            if (cleaned == message.Content) continue;

            message.Content = cleaned;
            repaired++;
        }

        if (repaired > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Applies any pending EF Core migrations. Called on API startup so a
    /// self-hoster's `docker compose pull` upgrade migrates the schema automatically.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        // Seed the singleton InstanceConfig row once, here — before the host starts serving
        // requests or running background services. Otherwise several callers can race to create
        // it on a fresh DB and collide on the primary key (SQLite's fast single writer surfaces
        // the race that SQL Server masked).
        if (!await db.InstanceConfig.AnyAsync(ct))
        {
            db.InstanceConfig.Add(new InstanceConfig
            {
                Id = InstanceConfig.SingletonId,
                IsConfigured = false,
                Features = InstanceConfig.DefaultFeatures(),
            });
            await db.SaveChangesAsync(ct);
        }

        await RepairLeakedFencesAsync(db, ct);

        // WAL lets readers proceed during writes and is a persistent setting stored in the
        // file, so setting it once here is enough. synchronous=NORMAL is the durable-enough,
        // faster pairing recommended with WAL.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);

        // Fail loud if the file is corrupt, so the operator knows to restore from a backup
        // rather than the app limping along on a damaged database.
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PersonaOS.Persistence");
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = (await cmd.ExecuteScalarAsync(ct))?.ToString();
            if (result == "ok")
                logger.LogInformation("SQLite integrity check passed.");
            else
                logger.LogError("SQLite integrity check FAILED: {Result}. Restore from a backup — see docs/BACKUP.md.", result);
        }
    }
}
