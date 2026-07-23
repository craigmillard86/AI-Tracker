using Hap.Domain.Org;
using Hap.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="BuMonthlyMetrics"/> (HAP-15, migration #8; data-model.md
/// "BUMonthlyMetrics"; FR-048). <see cref="SupportInternal"/>/<see cref="SupportCustomer"/> map to
/// native jsonb columns via EF Core 8's <c>OwnsOne(...).ToJson()</c> — matching data-model.md's
/// "support_internal (jsonb: ...)" / "support_customer (jsonb: ...)" spec exactly, no relational
/// shredding into extra columns or tables.
///
/// <para><b>Concurrency:</b> same reasoning as <c>BuAiDlcDeclarationConfiguration</c> — a same-month
/// resubmission race is correctly resolved last-write-wins under FR-048 (no ordering invariant to
/// protect), so no xmin token; the UNIQUE index on (BusinessUnitId, Month) is what stops a concurrent
/// Create-Create race from producing two rows for the same month.</para>
/// </summary>
public sealed class BuMonthlyMetricsConfiguration : IEntityTypeConfiguration<BuMonthlyMetrics>
{
    public void Configure(EntityTypeBuilder<BuMonthlyMetrics> e)
    {
        e.ToTable("bu_monthly_metrics");
        e.HasKey(x => x.Id);

        e.Property(x => x.BusinessUnitId).IsRequired();
        e.Property(x => x.Month).IsRequired();
        e.Property(x => x.SorCalledByOtherApps);
        e.Property(x => x.SubmittedBy).IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.UpdatedAt).IsRequired();

        e.OwnsOne(x => x.SupportInternal, si =>
        {
            si.ToJson();
            si.Property(p => p.TimeSavingsPct);
            si.Property(p => p.FewerPeopleNeeded);
            si.Property(p => p.SupportRatioImpact);
        });
        e.Navigation(x => x.SupportInternal).IsRequired();

        e.OwnsOne(x => x.SupportCustomer, sc =>
        {
            sc.ToJson();
            sc.Property(p => p.CustomersYtd);
            sc.Property(p => p.TicketsYtd);
            sc.Property(p => p.ResolvedByAiYtd);
            sc.Property(p => p.AiAssistedYtd);
        });
        e.Navigation(x => x.SupportCustomer).IsRequired();

        // One metrics row per BU per (first-of-month-normalised) month — the upsert invariant.
        e.HasIndex(x => new { x.BusinessUnitId, x.Month }).IsUnique();

        e.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // SubmittedBy is NOT an enforced FK — same reasoning as BuAiDlcDeclaration.DeclaredBy.
    }
}
