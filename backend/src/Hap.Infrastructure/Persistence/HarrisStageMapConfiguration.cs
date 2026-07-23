using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="HarrisStageMap"/> (HAP-13, migration #6; data-model.md "HarrisStageMap").
/// Seeded configuration rows (FR-064) — public <c>DbSet</c>, immutable entity. Both enums persist as
/// strings (readable in the DB, stable across value re-ordering). Unique index on
/// <see cref="HarrisStageMap.InternalStage"/> enforces "one Harris stage per internal stage" and is the
/// seeder's idempotency key.
/// </summary>
public sealed class HarrisStageMapConfiguration : IEntityTypeConfiguration<HarrisStageMap>
{
    public void Configure(EntityTypeBuilder<HarrisStageMap> e)
    {
        e.ToTable("harris_stage_map");
        e.HasKey(x => x.Id);
        e.Property(x => x.InternalStage).HasConversion<string>().IsRequired();
        e.Property(x => x.HarrisStage).HasConversion<string>().IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();
        e.HasIndex(x => x.InternalStage).IsUnique();
    }
}
