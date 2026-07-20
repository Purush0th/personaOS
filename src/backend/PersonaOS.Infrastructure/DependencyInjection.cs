using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Infrastructure.Ai;
using PersonaOS.Infrastructure.Auth;
using PersonaOS.Infrastructure.Persistence;
using PersonaOS.Infrastructure.Security;

namespace PersonaOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Binds every Application port to its Infrastructure adapter: EF Core persistence,
    /// Data Protection secrets, JWT creation, password hashing, and the Anthropic client.
    /// Connection string is read from configuration key "ConnectionStrings:PersonaOS".
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PersonaOS")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:PersonaOS'.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<IAiMessageStreamer, AnthropicMessageStreamer>();

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
    }
}
