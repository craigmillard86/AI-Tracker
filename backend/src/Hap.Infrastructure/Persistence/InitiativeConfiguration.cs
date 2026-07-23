using Hap.Domain.Org;
using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="Initiative"/> (HAP-13, migration #6, extended HAP-14 migration #7;
/// data-model.md "Initiative"). Register data — NOT individual assessment data — so it carries a public
/// <c>DbSet</c> and is readable outside the visibility seam.
///
/// <para>The five string-array fields (<c>functions_affected</c>, <c>dimensions_advanced</c>,
/// <c>regulatory_relevance</c>, <c>models_providers</c>, <c>vendors_tools</c> — the latter three added
/// HAP-14) map to PostgreSQL native <c>text[]</c> columns via Npgsql's primitive-collection support,
/// mutated only through the entity's domain methods (field access mode). <c>dimensions_advanced</c> holds
/// framework dimension KEYS, not FKs — a version-bound FK would break as the framework versions.</para>
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

        // HAP-14 governance panel (FR-030 — informational only, §4.2). DataSensitivity gets an explicit
        // HasDefaultValue(None): migration #7 ADDs this column to the already-created `initiatives`
        // table (unlike HAP-13's columns, which were present at CREATE TABLE time), so a NOT NULL string
        // column needs a real default to be safe against any pre-existing row — even though this build
        // has no production data (idempotent-migration discipline, §8.5).
        e.Property(x => x.DataSensitivity).HasConversion<string>().HasDefaultValue(DataSensitivity.None).IsRequired();
        e.Property(x => x.ApprovalStatus);
        e.Property(x => x.Approver);
        e.Property(x => x.OversightModel);
        e.Property(x => x.GovernanceNotes);

        // HAP-14 technology panel (FR-032).
        e.Property(x => x.UsesCogito).HasDefaultValue(false).IsRequired();

        // Native text[] columns, backed by the entity's private List<string> fields (Npgsql maps
        // IEnumerable<string> primitive collections to PostgreSQL arrays; field access so the
        // read-only IReadOnlyList facade stays intact). The three HAP-14 array columns get an explicit
        // empty-array SQL default for the same ADD-COLUMN-onto-an-existing-table reason as
        // DataSensitivity above — functions_affected/dimensions_advanced didn't need one (present at
        // CREATE TABLE time, migration #6).
        e.PrimitiveCollection(x => x.FunctionsAffected)
            .HasColumnName("functions_affected")
            .HasColumnType("text[]");
        e.PrimitiveCollection(x => x.DimensionsAdvanced)
            .HasColumnName("dimensions_advanced")
            .HasColumnType("text[]");
        e.PrimitiveCollection(x => x.RegulatoryRelevance)
            .HasColumnName("regulatory_relevance")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");
        e.PrimitiveCollection(x => x.ModelsProviders)
            .HasColumnName("models_providers")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");
        e.PrimitiveCollection(x => x.VendorsTools)
            .HasColumnName("vendors_tools")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");

        // Optimistic concurrency on CurrentStage (panel finding, HAP-14, mirroring
        // AssessmentEntityConfiguration's HAP-9 precedent). Two concurrent forward stage transitions
        // both read the same CurrentStage before either writes, so without a concurrency guard both
        // AdvanceStage calls succeed in-memory and the second SaveChangesAsync silently overwrites the
        // first — an effective backward net-movement with no 409, violating FR-028's forward-only
        // guarantee. Npgsql maps a `uint` property named `xmin` to Postgres's always-present system
        // column, so this needs NO migration and adds no DDL.
        e.Property<uint>("xmin").IsRowVersion();

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
