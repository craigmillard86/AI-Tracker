using Hap.Domain.Org;
using Hap.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="BuAiDlcDeclaration"/> (HAP-15, migration #8; data-model.md
/// "BUAIDLCDeclaration"; FR-047). Harris-reporting data — NOT individual assessment data — so it
/// carries a public <c>DbSet</c> and is readable outside the visibility seam.
///
/// <para><b>Concurrency: unique index, no xmin token — deliberate deviation from
/// <c>InitiativeConfiguration</c>'s xmin-on-CurrentStage precedent.</b> The plausible race here is two
/// concurrent same-week resubmissions for the same BU. Initiative's stage machine needs xmin because
/// silently letting the second writer clobber the first can regress a forward-only invariant (FR-028).
/// A declaration resubmission has no such ordering invariant: FR-047 treats "the declaration for this
/// BU this week" as a single mutable fact, and last-write-wins is the CORRECT resolution when two
/// people submit the same week's number — whichever save lands last is definitionally the BU's current
/// position, there is nothing to protect against being overwritten. The UNIQUE index on
/// (BusinessUnitId, WeekOf) is the invariant that actually matters: it stops a concurrent
/// Create-Create race (two simultaneous FIRST-time submissions for the same week, which the endpoint's
/// find-then-branch alone cannot prevent) from producing two rows — Postgres rejects the loser's INSERT
/// outright.</para>
/// </summary>
public sealed class BuAiDlcDeclarationConfiguration : IEntityTypeConfiguration<BuAiDlcDeclaration>
{
    public void Configure(EntityTypeBuilder<BuAiDlcDeclaration> e)
    {
        e.ToTable("bu_ai_dlc_declarations");
        e.HasKey(x => x.Id);

        e.Property(x => x.BusinessUnitId).IsRequired();
        e.Property(x => x.WeekOf).IsRequired();
        e.Property(x => x.DeclaredLevel).IsRequired();
        e.Property(x => x.NextLevelExpectedDate);
        e.Property(x => x.RagStatus).HasConversion<string>().IsRequired();
        e.Property(x => x.Note);
        e.Property(x => x.DeclaredBy).IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.UpdatedAt).IsRequired();

        // One declaration per BU per (Monday-normalised) week — the upsert invariant (FR-047 amended).
        e.HasIndex(x => new { x.BusinessUnitId, x.WeekOf }).IsUnique();

        e.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // DeclaredBy is NOT an enforced FK — same reasoning as Initiative.OwnerPersonId
        // (InitiativeConfiguration): a person reference the register-adjacent tables must not be
        // blocked or cascade-affected by directory churn.
    }
}
