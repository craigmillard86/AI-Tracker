using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure;

/// <summary>
/// The application database context. It carries no entities yet — the schema is
/// introduced migration-by-migration in later stories. It exists now so the
/// migration step in verify.sh has a context to target and no-ops cleanly.
/// </summary>
public class HapDbContext : DbContext
{
    public HapDbContext(DbContextOptions<HapDbContext> options)
        : base(options)
    {
    }
}
