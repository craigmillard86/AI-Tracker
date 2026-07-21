using Hap.Domain.Audit;
using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure;

/// <summary>
/// The application database context. Migration #1 (HAP-3) introduces the org model, the
/// manual-override layer, role grants, and the append-only audit log. Later stories chain
/// forward-only migrations behind this one.
///
/// Note the deliberate asymmetry on <see cref="AuditLogs"/>: the entity is immutable (no
/// setters) and no code path calls Update/Remove on this set — enforced by
/// <c>Hap.Architecture.Tests</c>. Rows are appended only through
/// <c>Hap.Infrastructure.Audit.AuditWriter</c>, which stages them on this same context so
/// they commit atomically with the audited business write (fail-closed).
/// </summary>
public class HapDbContext : DbContext
{
    public HapDbContext(DbContextOptions<HapDbContext> options)
        : base(options)
    {
    }

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<GroupOrg> Groups => Set<GroupOrg>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<OrgOverride> OrgOverrides => Set<OrgOverride>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Portfolio>(e =>
        {
            e.ToTable("portfolios");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<GroupOrg>(e =>
        {
            e.ToTable("groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => new { x.PortfolioId, x.Name }).IsUnique();
            e.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BusinessUnit>(e =>
        {
            e.ToTable("business_units");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<GroupOrg>().WithMany().HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("people");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalRef).IsRequired();
            e.Property(x => x.DisplayName).IsRequired();
            e.Property(x => x.Email).IsRequired();
            e.Property(x => x.JobTitle).IsRequired();
            e.Property(x => x.EmployeeType).HasConversion<string>().IsRequired();
            e.HasIndex(x => x.ExternalRef).IsUnique();
            e.HasIndex(x => x.BusinessUnitId);
            e.HasIndex(x => x.ManagerPersonId);
            e.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId)
                .OnDelete(DeleteBehavior.Restrict);
            // Self-reference: manager is another person. Never cascade — people are never deleted.
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.ManagerPersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrgOverride>(e =>
        {
            e.ToTable("org_overrides");
            e.HasKey(x => x.Id);
            // OrgOverride is immutable (get-only props): every property is listed explicitly so
            // EF includes it and binds the constructor — read-only properties are only auto-included
            // when configured or constructor-bound.
            e.Property(x => x.Field).HasConversion<string>().IsRequired();
            e.Property(x => x.OriginalValue);
            e.Property(x => x.OverrideValue).IsRequired();
            e.Property(x => x.Reason).IsRequired();
            e.Property(x => x.CreatedBy).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => x.PersonId);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleGrant>(e =>
        {
            e.ToTable("role_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasConversion<string>().IsRequired();
            e.Property(x => x.GrantedBy).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => x.PersonId);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            // Append-only, immutable entity — all properties get-only, listed explicitly so EF
            // binds the constructor (there is no setter to materialise into).
            e.Property(x => x.At).IsRequired();
            e.Property(x => x.Action).HasConversion<string>().IsRequired();
            e.Property(x => x.Detail).HasColumnType("jsonb").IsRequired();
            // Actor/subject reference people but are intentionally NOT enforced FKs: the audit
            // trail must never be blocked or cascade-affected by the state of the people table.
            e.Property(x => x.ActorPersonId);
            e.Property(x => x.SubjectPersonId);
            e.HasIndex(x => new { x.SubjectPersonId, x.At });
            e.HasIndex(x => x.At);
        });
    }
}
