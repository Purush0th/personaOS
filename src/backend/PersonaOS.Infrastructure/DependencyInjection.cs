using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
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
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        // AI providers: concrete adapters + a factory that selects one per InstanceConfig.
        services.AddSingleton<AnthropicMessageStreamer>();
        services.AddSingleton<OpenAiCompatibleMessageStreamer>();
        services.AddSingleton<IAiMessageStreamerFactory, AiMessageStreamerFactory>();
        // Push: no provider configured yet. Swap for the FCM adapter once Firebase
        // credentials exist — the dispatcher keeps reminders pending until then.
        services.AddSingleton<IPushSender, NullPushSender>();
        services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        // Nightly self-contained snapshot (db + docs + keyring). See BackupService.
        services.AddHostedService<BackupService>();

        return services;
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
