using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="InitiativeStageHistory"/> (HAP-14, migration #7; data-model.md
/// "InitiativeStageHistory"; FR-028). Immutable entity (get-only props) — every scalar is listed
/// explicitly so EF binds the constructor, matching <c>AuditLog</c>'s / <c>OrgOverride</c>'s convention
/// for append-only rows. Composite index on <c>(InitiativeId, EnteredAt)</c> is the ordered-read path the
/// detail endpoint uses to render the timeline oldest→newest.
///
/// <para>No DB trigger backstop for this story (see <see cref="InitiativeStageHistory"/>'s class doc) —
/// the EF mapping (no setters mapped, field access mode implied by the get-only properties) plus the
/// paired architecture test (<c>InitiativeStageHistoryAppendOnlyTests</c>, mirroring
/// <c>AuditAppendOnlyTests</c>) is the guarantee here.</para>
/// </summary>
public sealed class InitiativeStageHistoryConfiguration : IEntityTypeConfiguration<InitiativeStageHistory>
{
    public void Configure(EntityTypeBuilder<InitiativeStageHistory> e)
    {
        e.ToTable("initiative_stage_history");
        e.HasKey(x => x.Id);

        e.Property(x => x.InitiativeId).IsRequired();
        e.Property(x => x.Stage).HasConversion<string>().IsRequired();
        e.Property(x => x.PriorStage).HasConversion<string>();
        e.Property(x => x.EnteredAt).IsRequired();
        e.Property(x => x.EnteredBy).IsRequired();

        e.HasIndex(x => x.InitiativeId);
        e.HasIndex(x => new { x.InitiativeId, x.EnteredAt });

        // Register data (not individual assessment data) — the parent FK is Restrict, matching
        // Initiative's own convention: history must never be cascade-deleted or block on directory churn.
        e.HasOne<Initiative>().WithMany().HasForeignKey(x => x.InitiativeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
