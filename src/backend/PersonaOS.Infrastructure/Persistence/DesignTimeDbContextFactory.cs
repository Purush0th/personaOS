using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PersonaOS.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core CLI (`dotnet ef migrations add`, etc.) at design time.
/// The connection string is a placeholder — generating a migration inspects the
/// model, it does not connect to the database. Runtime wiring lives in the API host.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=data/personaos.db")
            .Options;

        return new AppDbContext(options);
    }
}
