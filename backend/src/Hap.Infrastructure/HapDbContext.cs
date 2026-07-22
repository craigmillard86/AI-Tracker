using Hap.Domain.Audit;
using Hap.Domain.Cycles;
using Hap.Domain.Frameworks;
using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure;

/// <summary>
/// The application database context. Migration #1 (HAP-3) introduces the org model, the
/// manual-override layer, role grants, and the append-only audit log. Migration #2 (HAP-6)
/// chains behind it with the framework engine (Framework/FrameworkVersion/Dimension/
/// LevelDescriptor — FR-001/FR-054). Migration #3 (HAP-7) chains behind that with cycle
/// management (Cycle/CycleInvitation/CycleLateOverride — FR-002/003/005). Migration #4 (HAP-8)
/// chains behind that with the assessment tables (data-model.md's assessment + score entities) —
/// mapped via <c>ApplyConfiguration</c> (see <c>Persistence.AssessmentConfiguration</c>) rather than
/// inline here, and deliberately WITHOUT a public DbSet: those tables are queried only through the
/// visibility seam's store (research D1). Later stories chain forward-only migrations behind this one.
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
    public DbSet<Framework> Frameworks => Set<Framework>();
    public DbSet<FrameworkVersion> FrameworkVersions => Set<FrameworkVersion>();
    public DbSet<Dimension> Dimensions => Set<Dimension>();
    public DbSet<LevelDescriptor> LevelDescriptors => Set<LevelDescriptor>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<CycleInvitation> CycleInvitations => Set<CycleInvitation>();
    public DbSet<CycleLateOverride> CycleLateOverrides => Set<CycleLateOverride>();
    public DbSet<Domain.Rollups.RollupSnapshot> RollupSnapshots => Set<Domain.Rollups.RollupSnapshot>();

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

        modelBuilder.Entity<Framework>(e =>
        {
            e.ToTable("frameworks");
            e.HasKey(x => x.Id);
            // Framework is immutable (get-only props): every scalar property is listed
            // explicitly so EF includes it and binds the constructor, matching OrgOverride.
            e.Property(x => x.Key).IsRequired();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Description);
            e.Property(x => x.Owner);
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<FrameworkVersion>(e =>
        {
            e.ToTable("framework_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().IsRequired();
            e.Property(x => x.SourceRef);
            e.Property(x => x.IsLocked).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.FrameworkId, x.VersionNumber }).IsUnique();
            e.HasOne<Framework>().WithMany().HasForeignKey(x => x.FrameworkId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Dimension>(e =>
        {
            e.ToTable("dimensions");
            e.HasKey(x => x.Id);
            // Dimension is immutable (get-only props): listed explicitly, as above.
            e.Property(x => x.Key).IsRequired();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.DisplayOrder).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.FrameworkVersionId, x.Key }).IsUnique();
            e.HasOne<FrameworkVersion>().WithMany().HasForeignKey(x => x.FrameworkVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LevelDescriptor>(e =>
        {
            e.ToTable("level_descriptors");
            e.HasKey(x => x.Id);
            // LevelDescriptor is immutable (get-only props): listed explicitly, as above.
            e.Property(x => x.Level).IsRequired();
            e.Property(x => x.LevelName).IsRequired();
            e.Property(x => x.DescriptorText).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.DimensionId, x.Level }).IsUnique();
            e.HasOne<Dimension>().WithMany().HasForeignKey(x => x.DimensionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cycle>(e =>
        {
            e.ToTable("cycles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.State).HasConversion<string>().IsRequired();
            e.Property(x => x.ContractorExclusionEnabled).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.HasIndex(x => new { x.FrameworkVersionId, x.State });
            e.HasOne<FrameworkVersion>().WithMany().HasForeignKey(x => x.FrameworkVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CycleInvitation>(e =>
        {
            e.ToTable("cycle_invitations");
            e.HasKey(x => x.Id);
            // CycleInvitation is immutable (get-only props): listed explicitly, as elsewhere.
            e.Property(x => x.Excluded).IsRequired();
            e.Property(x => x.ExcludedReason).HasConversion<string>();
            e.Property(x => x.CreatedAt).IsRequired();
            // One invitation row per person per cycle — invitation generation runs once at open
            // (FR-003) and must never double-write for the same (cycle, person) pair.
            e.HasIndex(x => new { x.CycleId, x.PersonId }).IsUnique();
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CycleLateOverride>(e =>
        {
            e.ToTable("cycle_late_overrides");
            e.HasKey(x => x.Id);
            // CycleLateOverride is immutable (get-only props): listed explicitly, as elsewhere.
            e.Property(x => x.GrantedByRole).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            // At most one active override per (cycle, person) — CycleService.GrantLateOverrideAsync
            // is idempotent against this exact constraint (returns the existing grant, never a
            // duplicate row).
            e.HasIndex(x => new { x.CycleId, x.PersonId }).IsUnique();
            e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.GrantedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // The assessment tables (HAP-8). Applied from a dedicated configuration class so this context file
        // never spells the bare assessment/score type tokens the seam-boundary guard forbids outside the
        // seam; that mapping file is the single allowlisted schema-config location. No DbSet is exposed —
        // the seam's store is the only query path (research D1).
        modelBuilder.ApplyConfiguration(new Persistence.AssessmentConfiguration());
        modelBuilder.ApplyConfiguration(new Persistence.AssessmentScoreConfiguration());

        // The rollup snapshot table (HAP-10, migration #5) — frozen aggregate output written at cycle
        // close (research D4). Aggregate output, not individual data, so unlike the assessment tables it
        // carries a public DbSet; append-only is enforced at the DB by the migration's triggers.
        modelBuilder.ApplyConfiguration(new Persistence.RollupSnapshotConfiguration());
    }
}
