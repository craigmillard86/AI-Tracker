using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="InitiativeNRLine"/> (HAP-14, migration #7; data-model.md
/// "InitiativeNRLine"; FR-029/FR-045). Unlike the append-only stage-history/weekly-update tables, this
/// entity is deletable — <c>ReferencedBySubmissionLineId</c> is the lock the delete endpoint checks
/// (409 once non-null; see <see cref="InitiativeNRLine"/>'s class doc for the HAP-16 stub note).
/// </summary>
public sealed class InitiativeNRLineConfiguration : IEntityTypeConfiguration<InitiativeNRLine>
{
    public void Configure(EntityTypeBuilder<InitiativeNRLine> e)
    {
        e.ToTable("initiative_nr_lines");
        e.HasKey(x => x.Id);

        e.Property(x => x.InitiativeId).IsRequired();
        e.Property(x => x.Year).IsRequired();
        e.Property(x => x.Direction).HasConversion<string>().IsRequired();
        e.Property(x => x.Recurrence).HasConversion<string>().IsRequired();
        e.Property(x => x.AmountUsd).HasColumnType("numeric(18,2)").IsRequired();
        e.Property(x => x.Description);
        e.Property(x => x.ReferencedBySubmissionLineId);

        e.HasIndex(x => x.InitiativeId);

        e.HasOne<Initiative>().WithMany().HasForeignKey(x => x.InitiativeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
