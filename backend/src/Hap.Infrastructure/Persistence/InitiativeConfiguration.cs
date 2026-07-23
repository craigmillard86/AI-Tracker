using Hap.Domain.Org;
using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="Initiative"/> (HAP-13, migration #6; data-model.md "Initiative"). Register
/// data — NOT individual assessment data — so it carries a public <c>DbSet</c> and is readable outside
/// the visibility seam.
///
/// <para>The two string-array fields (<c>functions_affected</c>, <c>dimensions_advanced</c>) map to
/// PostgreSQL native <c>text[]</c> columns via Npgsql's primitive-collection support, mutated only
/// through the entity's domain methods (field access mode). <c>dimensions_advanced</c> holds framework
/// dimension KEYS, not FKs — a version-bound FK would break as the framework versions.</para>
///
/// <para>Indexes support the FR-035 facet queries: (business_unit_id, current_stage) is the
/// data-model.md primary register index; category, risk tier, and AI-DLC level each get a plain index so
/// their facet filters are covered. No index on <c>dimensions_advanced</c> — the array-contains facet
/// runs unindexed, comfortably fine at register scale (initiative-count, not person-count); a GIN index
/// can be added later if the register grows large enough to need it.</para>
/// </summary>
public sealed class InitiativeConfiguration : IEntityTypeConfiguration<Initiative>
{
    public void Configure(EntityTypeBuilder<Initiative> e)
    {
        e.ToTable("initiatives");
        e.HasKey(x => x.Id);

        e.Property(x => x.BusinessUnitId).IsRequired();
        e.Property(x => x.Name).IsRequired();
        e.Property(x => x.Description);
        e.Property(x => x.SponsorPersonId);
        e.Property(x => x.OwnerPersonId).IsRequired();
        e.Property(x => x.CreatedByPersonId).IsRequired();
        e.Property(x => x.RegisteredAt).IsRequired();
        e.Property(x => x.CategoryId).IsRequired();
        e.Property(x => x.AiDlcLevel).IsRequired();
        e.Property(x => x.CurrentStage).HasConversion<string>().IsRequired();
        e.Property(x => x.RagStatus).HasConversion<string>().IsRequired();
        e.Property(x => x.LastUpdateAt).IsRequired();
        e.Property(x => x.CustomersInProduction);
        e.Property(x => x.RiskTier).HasConversion<string>().IsRequired();

        // Native text[] columns, backed by the entity's private List<string> fields (Npgsql maps
        // IEnumerable<string> primitive collections to PostgreSQL arrays; field access so the
        // read-only IReadOnlyList facade stays intact).
        e.PrimitiveCollection(x => x.FunctionsAffected)
            .HasColumnName("functions_affected")
            .HasColumnType("text[]");
        e.PrimitiveCollection(x => x.DimensionsAdvanced)
            .HasColumnName("dimensions_advanced")
            .HasColumnType("text[]");

        e.HasIndex(x => new { x.BusinessUnitId, x.CurrentStage });
        e.HasIndex(x => x.CategoryId);
        e.HasIndex(x => x.RiskTier);
        e.HasIndex(x => x.AiDlcLevel);

        e.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<HarrisCategory>().WithMany().HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        // Owner / sponsor / creator reference people but are intentionally NOT enforced FKs: the
        // register must not be blocked or cascade-affected by directory churn (a since-deactivated
        // owner still leaves a valid historical initiative row).
        e.Property(x => x.OwnerPersonId);
        e.Property(x => x.SponsorPersonId);
        e.Property(x => x.CreatedByPersonId);
    }
}
