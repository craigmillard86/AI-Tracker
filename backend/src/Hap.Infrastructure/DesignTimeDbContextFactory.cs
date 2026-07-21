using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hap.Infrastructure;

/// <summary>
/// Lets the EF Core CLI (`dotnet ef`) construct the context at design time without
/// booting the web host. The connection string comes from HAP_DB_CONNECTION so
/// verify.sh can point migrations at its disposable, random-port Postgres.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HapDbContext>
{
    public HapDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("HAP_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=hap;Username=hap;Password=hap";

        var options = new DbContextOptionsBuilder<HapDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HapDbContext(options);
    }
}
