using System.Text.Json;
using Hap.Domain.Rollups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hap.Infrastructure.Persistence;

/// <summary>
/// EF mapping for <see cref="RollupSnapshot"/> (HAP-10, migration #5; data-model.md RollupSnapshot,
/// research D4). Unlike the assessment tables this is aggregate output — NOT individual data — so it is
/// mapped with a public <c>DbSet</c> and may be read outside the seam; the raw scores it is derived from
/// were only ever read through the seam at close time. The three jsonb columns (per-dimension mean, floor
/// distribution, calibration delta) are serialised with System.Text.Json; each gets a value comparer so
/// EF models them correctly even though snapshots are insert-only (append-only, enforced at the DB by the
/// migration's triggers, mirroring <c>audit_log</c>).
/// </summary>
public sealed class RollupSnapshotConfiguration : IEntityTypeConfiguration<RollupSnapshot>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<RollupSnapshot> e)
    {
        e.ToTable("rollup_snapshots");
        e.HasKey(x => x.Id);
        e.Property(x => x.CycleId).IsRequired();
        e.Property(x => x.OrgNodeType).HasConversion<string>().IsRequired();
        e.Property(x => x.OrgNodeRef); // null only for the AllHig node
        e.Property(x => x.N).IsRequired();
        e.Property(x => x.CompletionDenominator).IsRequired();
        e.Property(x => x.CompletionPct).IsRequired();
        e.Property(x => x.UnmoderatedPct).IsRequired();
        e.Property(x => x.Suppressed).IsRequired();
        e.Property(x => x.SuppressionReason); // null when published
        e.Property(x => x.CreatedAt).IsRequired();

        MapJsonDictionary(e.Property(x => x.PerDimensionMean));
        MapJsonDictionary(e.Property(x => x.CalibrationDelta));

        e.Property(x => x.FloorLevelDistribution)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Dictionary<int, int>>(v, Json) ?? new Dictionary<int, int>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyDictionary<int, int>>(
                (a, b) => Equal(a, b),
                d => d.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                d => d.ToDictionary(kv => kv.Key, kv => kv.Value)));

        // Exactly one snapshot per (cycle, node type, node ref) — UNIQUE, the guard against a concurrent
        // double-close inserting two full snapshot sets (cycles has no concurrency token; the append-only
        // triggers would make such duplicates permanently undeletable and every SingleAsync reader throw).
        // A racing second close hits this and its whole transaction rolls back. NULLS NOT DISTINCT so the
        // AllHig row (node ref null) is deduplicated too — Postgres would otherwise treat two NULL refs as
        // distinct, leaving a window on a people-less cycle where only AllHig rows exist.
        e.HasIndex(x => new { x.CycleId, x.OrgNodeType, x.OrgNodeRef })
            .IsUnique()
            .AreNullsDistinct(false);

        e.HasOne<Domain.Cycles.Cycle>().WithMany().HasForeignKey(x => x.CycleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void MapJsonDictionary(PropertyBuilder<IReadOnlyDictionary<string, double>> property) =>
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Dictionary<string, double>>(v, Json) ?? new Dictionary<string, double>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyDictionary<string, double>>(
                (a, b) => Equal(a, b),
                d => d.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                d => d.ToDictionary(kv => kv.Key, kv => kv.Value)));

    private static bool Equal<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? a, IReadOnlyDictionary<TKey, TValue>? b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !EqualityComparer<TValue>.Default.Equals(kv.Value, other))
            {
                return false;
            }
        }
        return true;
    }
}
