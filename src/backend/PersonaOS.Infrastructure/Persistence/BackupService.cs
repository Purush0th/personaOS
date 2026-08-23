using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PersonaOS.Infrastructure.Persistence;

/// <summary>
/// Takes a nightly, self-contained backup of the whole instance: a clean SQLite
/// snapshot (VACUUM INTO — safe while the app runs) plus the uploaded documents and
/// the Data Protection keyring, so the ciphertext API key and its key travel together.
/// Keeps the most recent <c>Backup:KeepDays</c> snapshots.
///
/// This protects against corruption and fat-finger deletes. For real disaster
/// resilience the backup path should live on a DIFFERENT disk/host than the database
/// (mount it elsewhere) or be paired with off-host replication — see docs/BACKUP.md.
/// Failures never crash the host; they are logged and retried next tick.
/// </summary>
public class BackupService(IConfiguration configuration, ILogger<BackupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BackupEvery = TimeSpan.FromHours(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Backup:Enabled", true))
        {
            logger.LogInformation("Backup service disabled (Backup:Enabled=false).");
            return;
        }

        var dbPath = ResolveDbPath(configuration);
        var backupRoot = configuration["Backup:Path"];
        if (string.IsNullOrWhiteSpace(backupRoot))
            backupRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");

        var keepDays = configuration.GetValue("Backup:KeepDays", 14);
        var docsPath = configuration["Documents:StoragePath"];
        var keysPath = configuration["DataProtection:KeysPath"];

        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                if (IsDue(backupRoot))
                    RunBackup(dbPath, backupRoot, docsPath, keysPath, keepDays);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nightly backup failed; will retry next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>True when there's no snapshot newer than <see cref="BackupEvery"/>.</summary>
    private static bool IsDue(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) return true;
        var newest = Directory.GetDirectories(backupRoot)
            .Select(d => Directory.GetLastWriteTimeUtc(d))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return DateTime.UtcNow - newest >= BackupEvery;
    }

    private void RunBackup(string dbPath, string backupRoot, string? docsPath, string? keysPath, int keepDays)
    {
        if (!File.Exists(dbPath))
        {
            logger.LogWarning("Backup skipped: database file not found at {DbPath}.", dbPath);
            return;
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(backupRoot, stamp);
        Directory.CreateDirectory(dest);

        // Clean single-file snapshot of the committed state, safe during normal operation.
        var snapshot = Path.Combine(dest, "personaos.db").Replace("'", "''");
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{snapshot}';";
            cmd.ExecuteNonQuery();
        }

        // The keyring MUST travel with the db, or the encrypted API key can't be decrypted.
        CopyDirectory(keysPath, Path.Combine(dest, "dp-keys"));
        CopyDirectory(docsPath, Path.Combine(dest, "docs-storage"));

        Prune(backupRoot, keepDays);
        logger.LogInformation("Backup written to {Dest}.", dest);
    }

    private static void Prune(string backupRoot, int keepDays)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(keepDays);
        foreach (var dir in Directory.GetDirectories(backupRoot))
        {
            if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                Directory.Delete(dir, recursive: true);
        }
    }

    private static void CopyDirectory(string? source, string dest)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Resolves the SQLite file path from Database:Path, or the connection string.</summary>
    internal static string ResolveDbPath(IConfiguration configuration)
    {
        var path = configuration["Database:Path"];
        if (!string.IsNullOrWhiteSpace(path)) return path;

        var connectionString = configuration.GetConnectionString("PersonaOS");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource)) return dataSource;
        }
        return Path.Combine("data", "personaos.db");
    }
}
