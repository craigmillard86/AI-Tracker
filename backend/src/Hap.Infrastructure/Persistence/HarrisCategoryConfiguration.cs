using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="HarrisCategory"/> (HAP-13, migration #6; data-model.md "HarrisCategory").
/// Seeded reference data — a public <c>DbSet</c>, readable anywhere (not seam-guarded). Immutable
/// entity (get-only props), so every scalar is listed explicitly for EF to bind the constructor,
/// matching <c>HapDbContext</c>'s framework-table convention. Unique index on <see cref="HarrisCategory.Key"/>
/// is the seeder's idempotency key.
/// </summary>
public sealed class HarrisCategoryConfiguration : IEntityTypeConfiguration<HarrisCategory>
{
    public void Configure(EntityTypeBuilder<HarrisCategory> e)
    {
        e.ToTable("harris_categories");
        e.HasKey(x => x.Id);
        e.Property(x => x.Key).IsRequired();
        e.Property(x => x.Name).IsRequired();
        e.Property(x => x.GroupReported).IsRequired();
        e.Property(x => x.CustomerDeployed).IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();
        e.HasIndex(x => x.Key).IsUnique();
    }
}
