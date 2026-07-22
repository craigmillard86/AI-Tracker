namespace Hap.Domain.Rollups;

/// <summary>
/// An immutable, append-only aggregate of one org node's moderated maturity, frozen at cycle close
/// (data-model.md "RollupSnapshot"; research D4). Trend/history reads consume snapshots; the live
/// dashboard recomputes open cycles on the fly. Written once by the cycle-close processor and NEVER
/// updated — there is deliberately no mutator and no state-changing method (the append-only guarantee is
/// re-asserted at the DB layer by migration #5's triggers, mirroring <c>audit_log</c>). Because the
/// verdict and every figure are frozen here, later org shrinkage cannot retro-expose a group (FR-071)
/// and a recomputation cannot silently unsuppress history (research D2).
///
/// <para><b>Two populations, tracked separately and reconciled independently</b> (FR-024 / root spec
/// §3.5, panel B1). <see cref="N"/> is the <i>scored population</i> — everyone who submitted, INCLUDING a
/// mid-cycle leaver whose moderated/auto-adopted work still counts toward the means and the floor
/// distribution. <see cref="CompletionDenominator"/> is the separate <i>completion</i> base — active,
/// invited people at close — which a leaver has left. The two never share a field precisely so a leaver
/// can raise the scored-population n while lowering the completion denominator, and both still reconcile.</para>
/// </summary>
public sealed class RollupSnapshot
{
    public Guid Id { get; private set; }
    public Guid CycleId { get; private set; }

    public OrgNodeType OrgNodeType { get; private set; }

    /// <summary>The node's identity within its type: a manager id (Team), BU/Group/Portfolio id, or
    /// <c>null</c> for the single <see cref="OrgNodeType.AllHig"/> node.</summary>
    public Guid? OrgNodeRef { get; private set; }

    /// <summary>Scored population — the count of people with a moderated or auto-adopted assessment in this
    /// node's scope. NOT the completion base (see class doc + <see cref="CompletionDenominator"/>).</summary>
    public int N { get; private set; }

    /// <summary>Mean of the moderated (score-of-record) value per dimension, keyed by dimension key,
    /// over the scored population, each to 2dp (FR-015). jsonb.</summary>
    public IReadOnlyDictionary<string, double> PerDimensionMean { get; private set; } =
        new Dictionary<string, double>();

    /// <summary>Count of the scored population at each floor level (min dimension score; FR-016), keyed by
    /// level 0–3. jsonb.</summary>
    public IReadOnlyDictionary<int, int> FloorLevelDistribution { get; private set; } =
        new Dictionary<int, int>();

    /// <summary>The completion base: active, invited people in this node's scope at close — leavers
    /// EXCLUDED (FR-024). Tracked separately from <see cref="N"/> so both reconcile independently.</summary>
    public int CompletionDenominator { get; private set; }

    /// <summary>Fraction 0–1 of the completion base that submitted (FR-019). 0 when the base is empty.</summary>
    public double CompletionPct { get; private set; }

    /// <summary>Fraction 0–1 of the scored population whose assessment was auto-adopted rather than
    /// manager-moderated (FR-068). 0 when the scored population is empty.</summary>
    public double UnmoderatedPct { get; private set; }

    /// <summary>Mean |self − manager| per dimension, keyed by dimension key, over the scored population
    /// EXCLUDING auto-adopted rows (their delta is definitionally zero; FR-068/FR-011). jsonb.</summary>
    public IReadOnlyDictionary<string, double> CalibrationDelta { get; private set; } =
        new Dictionary<string, double>();

    /// <summary>Whether this aggregate is suppressed on the read path (FR-014). Frozen at close: a later
    /// org change never flips it (FR-071 / research D2).</summary>
    public bool Suppressed { get; private set; }

    /// <summary>Why it was suppressed (<c>N&lt;4</c> | <c>Complement</c>), or <c>null</c> when published.
    /// A string, not the seam's enum, so the domain snapshot carries no dependency on the seam.</summary>
    public string? SuppressionReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // EF materialisation constructor. Private so the only construction path is the Create factory —
    // there is no way to make a snapshot in a mutable/partially-built state.
    private RollupSnapshot()
    {
    }

    /// <summary>Builds a frozen snapshot for one node. All figures are supplied pre-computed by the
    /// close processor (from <c>Hap.Domain.Scoring</c>); this factory only fixes identity and freezes
    /// the values. <paramref name="orgNodeRef"/> is null only for <see cref="OrgNodeType.AllHig"/>.</summary>
    public static RollupSnapshot Create(
        Guid cycleId,
        OrgNodeType orgNodeType,
        Guid? orgNodeRef,
        int n,
        IReadOnlyDictionary<string, double> perDimensionMean,
        IReadOnlyDictionary<int, int> floorLevelDistribution,
        int completionDenominator,
        double completionPct,
        double unmoderatedPct,
        IReadOnlyDictionary<string, double> calibrationDelta,
        bool suppressed,
        string? suppressionReason) =>
        new()
        {
            Id = Guid.NewGuid(),
            CycleId = cycleId,
            OrgNodeType = orgNodeType,
            OrgNodeRef = orgNodeRef,
            N = n,
            PerDimensionMean = perDimensionMean,
            FloorLevelDistribution = floorLevelDistribution,
            CompletionDenominator = completionDenominator,
            CompletionPct = completionPct,
            UnmoderatedPct = unmoderatedPct,
            CalibrationDelta = calibrationDelta,
            Suppressed = suppressed,
            SuppressionReason = suppressionReason,
            CreatedAt = DateTime.UtcNow,
        };
}
