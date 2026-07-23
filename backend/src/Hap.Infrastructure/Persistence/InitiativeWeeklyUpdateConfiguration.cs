using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="InitiativeWeeklyUpdate"/> (HAP-14, migration #7; data-model.md
/// "InitiativeWeeklyUpdate"; FR-033/FR-037). Immutable entity — every scalar listed explicitly, mirroring
/// <see cref="InitiativeStageHistoryConfiguration"/>. Composite index on <c>(InitiativeId, CreatedAt)</c>
/// supports the detail endpoint's newest-first update trail.
/// </summary>
public sealed class InitiativeWeeklyUpdateConfiguration : IEntityTypeConfiguration<InitiativeWeeklyUpdate>
{
    public void Configure(EntityTypeBuilder<InitiativeWeeklyUpdate> e)
    {
        e.ToTable("initiative_weekly_updates");
        e.HasKey(x => x.Id);

        e.Property(x => x.InitiativeId).IsRequired();
        e.Property(x => x.RagStatus).HasConversion<string>().IsRequired();
        e.Property(x => x.Note);
        e.Property(x => x.CreatedBy).IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();

        e.HasIndex(x => x.InitiativeId);
        e.HasIndex(x => new { x.InitiativeId, x.CreatedAt });

        e.HasOne<Initiative>().WithMany().HasForeignKey(x => x.InitiativeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
