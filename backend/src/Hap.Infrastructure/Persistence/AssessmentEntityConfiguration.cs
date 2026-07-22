using Hap.Domain.Assessments;
using Hap.Domain.Cycles;
using Hap.Domain.Frameworks;
using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for the assessment tables (HAP-8, migration #4; data-model.md Assessment +
/// AssessmentScore). Kept OUT of <see cref="HapDbContext"/>'s <c>OnModelCreating</c> body and applied
/// via <c>ApplyConfiguration</c> so the context file never spells the bare <c>Assessment</c>/
/// <c>AssessmentScore</c> type tokens — this is a schema-mapping location, not a query path, and is the
/// single allowlisted exception in <c>SeamBoundaryTests</c> alongside the domain definition and the
/// generated migrations. No public <c>DbSet</c> is exposed for these entities; the seam's store reads
/// them via <c>db.Set&lt;&gt;()</c>, keeping every query path inside the visibility seam (research D1).
/// </summary>
public sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> e)
    {
        e.ToTable("assessments");
        e.HasKey(x => x.Id);
        e.Property(x => x.State).HasConversion<string>().IsRequired();
        e.Property(x => x.SubmittedAt);
        e.Property(x => x.ModeratedAt);
        e.Property(x => x.ModeratedByPersonId);
        e.Property(x => x.Unmoderated).IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();

        // One assessment per person per cycle (data-model.md invariant) — enforced at the DB.
        e.HasIndex(x => new { x.CycleId, x.PersonId }).IsUnique();
        e.HasIndex(x => x.PersonId);

        e.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        // ModeratedByPersonId is a nullable, non-enforced reference (the moderator of record may differ
        // from the line manager after FR-070 escalation, and the audit trail must never be blocked by
        // people-table state — mirrors the audit_log convention).
        e.Property(x => x.ModeratedByPersonId);
    }
}

/// <summary>EF mapping for <see cref="AssessmentScore"/> (one row per assessment per dimension).</summary>
public sealed class AssessmentScoreConfiguration : IEntityTypeConfiguration<AssessmentScore>
{
    public void Configure(EntityTypeBuilder<AssessmentScore> e)
    {
        e.ToTable("assessment_scores");
        e.HasKey(x => x.Id);
        e.Property(x => x.SelfScore).IsRequired();
        e.Property(x => x.SelfEvidence).IsRequired(false);
        e.Property(x => x.ManagerScore).IsRequired(false);
        e.Property(x => x.ManagerComment).IsRequired(false);

        // One score row per (assessment, dimension) — the upsert relies on this uniqueness.
        e.HasIndex(x => new { x.AssessmentId, x.DimensionId }).IsUnique();

        e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade); // scores belong to their assessment
        e.HasOne<Dimension>().WithMany().HasForeignKey(x => x.DimensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
