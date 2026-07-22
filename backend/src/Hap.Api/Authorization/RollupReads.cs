using Hap.Api.Identity;
using Hap.Domain.Cycles;
using Hap.Domain.Org;
using Hap.Domain.Rollups;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>The published form of one aggregate figure-set — a real figure or a suppressed placeholder.
/// A <see cref="Suppressed"/> result carries ONLY the reason and NEVER a number (F2 / FR-071): the type makes
/// "a suppressed aggregate can never leak a figure" a compile-time guarantee, mirroring
/// <see cref="AggregateOutcome"/> but carrying the full dashboard figure-set for a published node.</summary>
public abstract record AggregateReadResult
{
    private AggregateReadResult() { }

    /// <summary>A visible aggregate. Carries every figure — only ever produced for a node whose FROZEN or
    /// live suppression verdict is "published".</summary>
    public sealed record Published(AggregateFigures Figures) : AggregateReadResult;

    /// <summary>A suppressed aggregate. Reason only — no n, no mean, no distribution (FR-071).</summary>
    public sealed record Suppressed(string Reason) : AggregateReadResult;

    public bool IsSuppressed => this is Suppressed;

    /// <summary>
    /// The SINGLE projection from a (verdict + figures) pair to a read result — the one place F2 is enforced.
    /// When suppressed, <paramref name="figures"/> is NEVER invoked, so no numeric field is even read for a
    /// suppressed node, let alone serialised. A <see cref="RollupReadGuardTests"/> case passes a throwing
    /// factory to prove it. Route every snapshot/live read through here; never map an entity's figures directly.
    /// </summary>
    public static AggregateReadResult Project(bool suppressed, string? reason, Func<AggregateFigures> figures) =>
        suppressed
            ? new Suppressed(string.IsNullOrEmpty(reason) ? "N<4" : reason)
            : new Published(figures());
}

/// <summary>The dashboard figure-set for one published node (FR-015/016/019/068). Reconcilable to raw rows:
/// the reconciliation guard recomputes each from moderated scores and asserts equality.</summary>
public sealed record AggregateFigures(
    int N,
    IReadOnlyDictionary<string, double> PerDimensionMean,
    IReadOnlyDictionary<int, int> FloorLevelDistribution,
    double CompletionPct,
    double UnmoderatedPct);

/// <summary>One framework dimension's identity (key/name/order) — framework DATA, not aggregate data, so it is
/// safe to return for any node including a suppressed one; the UI uses it to label bars.</summary>
public sealed record DimensionMetaView(string Key, string Name, int DisplayOrder);

/// <summary>One point on a node's cross-cycle trend (FR-017): a closed cycle and its (projected) figure-set —
/// itself <see cref="AggregateReadResult.Suppressed"/> if that cycle's node was suppressed.</summary>
public sealed record TrendPointView(Guid CycleId, string CycleName, AggregateReadResult Result);

/// <summary>A node's aggregate dashboard/rollup as the seam assembles it: identity, the current cycle and
/// whether it was computed live (open) or read from a frozen snapshot (closed), the current figure-set
/// (published or suppressed), the framework dimension metadata, and the closed-cycle trend.</summary>
public sealed record NodeAggregateView(
    OrgNodeType NodeType,
    Guid? NodeRef,
    string NodeName,
    Guid CycleId,
    string CycleName,
    string CycleState,
    bool Live,
    AggregateReadResult Current,
    IReadOnlyList<DimensionMetaView> Dimensions,
    IReadOnlyList<TrendPointView> Trend);

/// <summary>Thrown when a caller requests an aggregate for a node outside their visibility scope. The endpoint
/// maps it to 404 (existence-leak convention) with no data returned.</summary>
public sealed class AggregateAccessDeniedException : Exception
{
    public AggregateAccessDeniedException(Guid callerId, OrgNodeType nodeType, Guid? nodeRef)
        : base($"Caller {callerId} may not read the {nodeType} aggregate {nodeRef?.ToString() ?? "(all-HIG)"}.")
    {
    }
}

/// <summary>
/// The aggregate-read gateway (HAP-11) — the aggregate analogue of <see cref="AssessmentReads"/>. Every rollup
/// dashboard read (BU / org node / own team) funnels through here. Two guarantees it holds, both L3:
///
/// <para><b>Scope (existence-leak, FR-022/FR-025 clause 2).</b> A caller sees an aggregate only for a node in
/// their visibility scope. Above-BU aggregate scope is resolved from the hierarchy anchors
/// (<see cref="HierarchyRoleResolver"/>) plus explicit grants (HIG Executive → all-HIG, BU delegate → that BU)
/// — the FR-022 hierarchy-derived-visibility model (Q-024, provisional; owner ratification at G1). This is
/// STRICTLY the aggregate scope: the individual-read gate (<see cref="AssessmentReads"/>) is untouched and no
/// individual score is reachable through any hierarchy label here, and every figure is N&lt;4 + complement
/// suppressed so a mis-scoped node still leaks no individual (Q-024 rationale). Out of scope → 404, no data.</para>
///
/// <para><b>Suppression projection (F2 / FR-071).</b> Open cycles compute live via the shared
/// <see cref="RollupPipeline"/> (same figures the close snapshot froze — agreement by construction, research
/// D4); closed cycles read the frozen <see cref="RollupSnapshot"/> (public DbSet). EITHER path projects through
/// <see cref="AggregateReadResult.Project"/>, so a suppressed node's N/mean/distribution are never read into a
/// DTO — the F2 carry-forward this story closes.</para>
/// </summary>
public sealed class RollupReads
{
    private readonly HapDbContext _db;
    private readonly OrgGraphLoader _graphLoader;
    private readonly RollupPipeline _pipeline;
    private readonly HierarchyRoleResolver _hierarchy;

    public RollupReads(
        HapDbContext db, OrgGraphLoader graphLoader, RollupPipeline pipeline, HierarchyRoleResolver hierarchy)
    {
        _db = db;
        _graphLoader = graphLoader;
        _pipeline = pipeline;
        _hierarchy = hierarchy;
    }

    /// <summary>BU Lead dashboard (contracts/api.md GET /api/bus/{buId}/dashboard). Scope: the caller's own BU
    /// (BU delegate grant or hierarchy BU Lead), any BU in their group/portfolio (hierarchy Group/Portfolio
    /// Leader), or any BU (HIG Executive). Out of scope → <see cref="AggregateAccessDeniedException"/> (404).</summary>
    public async Task<NodeAggregateView> ReadBuDashboardAsync(Guid callerPersonId, Guid buId, CancellationToken ct = default)
    {
        var (graph, scope) = await ScopeAsync(callerPersonId, ct);
        if (!scope.CanSeeBu(buId))
        {
            throw new AggregateAccessDeniedException(callerPersonId, OrgNodeType.Bu, buId);
        }
        return await ReadNodeAsync(graph, OrgNodeType.Bu, buId, ct);
    }

    /// <summary>Group / Portfolio / AllHig rollup (contracts/api.md GET /api/org/{nodeType}/{nodeId}/rollup —
    /// aggregates only, no individual endpoint exists at these scopes by construction, FR-025). BU is also
    /// permitted here for uniformity (same scope rule as the dashboard). Out of scope → 404.</summary>
    public async Task<NodeAggregateView> ReadOrgRollupAsync(
        Guid callerPersonId, OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct = default)
    {
        var (graph, scope) = await ScopeAsync(callerPersonId, ct);
        var allowed = nodeType switch
        {
            OrgNodeType.Bu => nodeRef is Guid bu && scope.CanSeeBu(bu),
            OrgNodeType.Group => nodeRef is Guid g && scope.CanSeeGroup(g),
            OrgNodeType.Portfolio => nodeRef is Guid p && scope.CanSeePortfolio(p),
            OrgNodeType.AllHig => scope.AllHig,
            // Team is not an org-rollup surface — it is reachable only via /api/me/team/summary (own team).
            _ => false,
        };
        if (!allowed)
        {
            throw new AggregateAccessDeniedException(callerPersonId, nodeType, nodeRef);
        }
        return await ReadNodeAsync(graph, nodeType, nodeType == OrgNodeType.AllHig ? null : nodeRef, ct);
    }

    /// <summary>Own-team aggregate (contracts/api.md GET /api/me/team/summary — "own team aggregate only").
    /// The team is the caller's own — their own team if they manage one, else their manager's team — so no
    /// cross-scope check is needed; suppression still applies (a &lt;4 team returns Suppressed). A caller with
    /// no team (manager-less and not a manager) → <see cref="AggregateAccessDeniedException"/> (404).</summary>
    public async Task<NodeAggregateView> ReadTeamSummaryAsync(Guid callerPersonId, CancellationToken ct = default)
    {
        var graph = await _graphLoader.LoadAsync(ct);
        var self = graph.Find(callerPersonId);
        var teamManagerId = graph.HasDirectReports(callerPersonId)
            ? callerPersonId                // a manager sees their own team
            : self?.ManagerPersonId;        // otherwise their manager's team (FR spec §2 "own team")
        if (teamManagerId is null)
        {
            throw new AggregateAccessDeniedException(callerPersonId, OrgNodeType.Team, null);
        }
        return await ReadNodeAsync(graph, OrgNodeType.Team, teamManagerId, ct);
    }

    /// <summary>
    /// The caller's DEFAULT dashboard node (HAP-11 BB2; contracts/api.md GET /api/me/dashboard). Resolves the
    /// richest node the caller leads — HIG Executive → all-HIG; Portfolio/Group Leader → their node; BU Lead /
    /// BU delegate → their BU; otherwise a plain manager/individual → their own team — and routes through the
    /// SAME scope-checked read path as the addressed endpoints, so the router-less shell reaches each persona's
    /// scope with one call. A caller with no aggregate to show (no leadership, no team) → 404.
    /// </summary>
    public async Task<NodeAggregateView> ReadDefaultDashboardAsync(Guid callerPersonId, CancellationToken ct = default)
    {
        var (type, nodeRef) = await ResolveDefaultNodeAsync(callerPersonId, ct);
        return type switch
        {
            OrgNodeType.AllHig => await ReadOrgRollupAsync(callerPersonId, OrgNodeType.AllHig, null, ct),
            OrgNodeType.Portfolio => await ReadOrgRollupAsync(callerPersonId, OrgNodeType.Portfolio, nodeRef, ct),
            OrgNodeType.Group => await ReadOrgRollupAsync(callerPersonId, OrgNodeType.Group, nodeRef, ct),
            OrgNodeType.Bu => await ReadBuDashboardAsync(callerPersonId, nodeRef!.Value, ct),
            OrgNodeType.Team => await ReadTeamSummaryAsync(callerPersonId, ct),
            _ => throw new AggregateAccessDeniedException(callerPersonId, OrgNodeType.Team, null),
        };
    }

    /// <summary>Resolve the caller's default node — the richest scope they lead. Uses the SAME hierarchy
    /// anchors + explicit grants as <see cref="ScopeAsync"/> (Q-024). Falls back to their own team, then to
    /// "no node" (a null type → 404 → the UI's empty state).</summary>
    private async Task<(OrgNodeType? Type, Guid? Ref)> ResolveDefaultNodeAsync(Guid callerPersonId, CancellationToken ct)
    {
        var grants = await _db.RoleGrants
            .Where(g => g.PersonId == callerPersonId)
            .Select(g => new { g.Role, g.BusinessUnitId })
            .ToListAsync(ct);
        if (grants.Any(g => g.Role == OrgRole.HigExecutive))
        {
            return (OrgNodeType.AllHig, null);
        }

        var roles = await _hierarchy.ResolveAsync(callerPersonId, ct);
        if (roles.PortfolioLeaderOfPortfolioId is Guid p)
        {
            return (OrgNodeType.Portfolio, p);
        }
        if (roles.GroupLeaderOfGroupId is Guid g)
        {
            return (OrgNodeType.Group, g);
        }
        if (roles.BuLeadOfBusinessUnitId is Guid b)
        {
            return (OrgNodeType.Bu, b);
        }
        var buDelegate = grants.FirstOrDefault(x => x.Role == OrgRole.BuDelegate && x.BusinessUnitId is not null);
        if (buDelegate is not null)
        {
            return (OrgNodeType.Bu, buDelegate.BusinessUnitId);
        }

        var graph = await _graphLoader.LoadAsync(ct);
        var teamManagerId = graph.HasDirectReports(callerPersonId)
            ? callerPersonId
            : graph.Find(callerPersonId)?.ManagerPersonId;
        return teamManagerId is Guid tm ? (OrgNodeType.Team, tm) : (null, null);
    }

    // === scope resolution =============================================================================

    private async Task<(OrgGraph Graph, AggregateScope Scope)> ScopeAsync(Guid callerPersonId, CancellationToken ct)
    {
        var graph = await _graphLoader.LoadAsync(ct);

        var grants = await _db.RoleGrants
            .Where(g => g.PersonId == callerPersonId)
            .Select(g => new { g.Role, g.BusinessUnitId })
            .ToListAsync(ct);

        // Explicit grants first. HIG Executive → all-HIG (the consolidated view, FR-025 §2 aggregates-only).
        if (grants.Any(g => g.Role == OrgRole.HigExecutive))
        {
            return (graph, AggregateScope.Everything());
        }

        var buIds = new HashSet<Guid>();
        var groupIds = new HashSet<Guid>();
        var portfolioIds = new HashSet<Guid>();

        // BU delegate grants → those BUs (re-read from the DB, never the cookie — HAP-4 A3).
        foreach (var bu in grants.Where(g => g.Role == OrgRole.BuDelegate && g.BusinessUnitId is not null))
        {
            buIds.Add(bu.BusinessUnitId!.Value);
        }

        // Hierarchy-derived leadership (FR-022; Q-024 provisional — aggregate scope only, never individual
        // reads). The resolver names the exact unit led; a Group/Portfolio Leader's BUs are expanded via the
        // structural containment FKs so a BU-addressed dashboard request within their span resolves.
        var roles = await _hierarchy.ResolveAsync(callerPersonId, ct);
        if (roles.BuLeadOfBusinessUnitId is Guid buLead)
        {
            buIds.Add(buLead);
        }
        if (roles.GroupLeaderOfGroupId is Guid groupLead)
        {
            groupIds.Add(groupLead);
            foreach (var b in graph.BusInGroup(groupLead))
            {
                buIds.Add(b);
            }
        }
        if (roles.PortfolioLeaderOfPortfolioId is Guid portfolioLead)
        {
            portfolioIds.Add(portfolioLead);
            foreach (var b in graph.BusInPortfolio(portfolioLead))
            {
                buIds.Add(b);
            }
            // groups within the portfolio
            foreach (var b in graph.BusInPortfolio(portfolioLead))
            {
                if (graph.GroupOfBu(b) is Guid g)
                {
                    groupIds.Add(g);
                }
            }
        }

        return (graph, new AggregateScope(false, buIds, groupIds, portfolioIds));
    }

    // === the dual-path node read + projection =========================================================

    private async Task<NodeAggregateView> ReadNodeAsync(
        OrgGraph graph, OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct)
    {
        // Existence check (existence-leak convention): an all-HIG-scoped caller passes the scope gate for ANY
        // BU/group/portfolio id, so a request for a NONEXISTENT node must 404 rather than return a hollow
        // {suppressed} 200. Team refs are the caller's own (always exist); AllHig is the single null-ref node.
        if (!await NodeExistsAsync(nodeType, nodeRef, ct))
        {
            throw new AggregateAccessDeniedException(Guid.Empty, nodeType, nodeRef);
        }

        var cycle = await SeamCycleResolver.CurrentCycleAsync(_db, ct);
        var live = cycle.State == CycleState.Open;

        var current = live
            ? await ReadLiveAsync(graph, cycle, nodeType, nodeRef, ct)
            : await ReadSnapshotAsync(cycle.Id, nodeType, nodeRef, ct);

        var dimensions = await DimensionsAsync(cycle.FrameworkVersionId, ct);
        var trend = await TrendAsync(cycle, nodeType, nodeRef, ct);
        var nodeName = await NodeNameAsync(nodeType, nodeRef, ct);

        return new NodeAggregateView(
            nodeType, nodeRef, nodeName, cycle.Id, cycle.Name, cycle.State.ToString(), live, current, dimensions, trend);
    }

    /// <summary>Live open-cycle figures: run the shared pipeline over the whole tree, pick the requested node,
    /// project through its live suppression verdict. A node absent from the result set (0 scored people) is
    /// below threshold and returns Suppressed — never a fabricated zero.</summary>
    private async Task<AggregateReadResult> ReadLiveAsync(
        OrgGraph graph, Cycle cycle, OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct)
    {
        var personInputs = await _pipeline.BuildPersonInputsAsync(_db, graph, cycle, tracked: false, ct);
        var nodes = _pipeline.ComputeNodes(graph, personInputs);
        var node = nodes.FirstOrDefault(n => n.Type == nodeType && n.Ref == nodeRef);
        if (node is null)
        {
            return new AggregateReadResult.Suppressed("N<4");
        }
        return AggregateReadResult.Project(node.Suppressed, node.SuppressionReason, () => Figures(node.Rollup));
    }

    /// <summary>Closed-cycle figures: read the frozen snapshot (public DbSet) and project through its frozen
    /// verdict. No snapshot for the node (0 people at close) → Suppressed.</summary>
    private async Task<AggregateReadResult> ReadSnapshotAsync(
        Guid cycleId, OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct)
    {
        var snap = await _db.RollupSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.CycleId == cycleId && s.OrgNodeType == nodeType && s.OrgNodeRef == nodeRef, ct);
        if (snap is null)
        {
            return new AggregateReadResult.Suppressed("N<4");
        }
        return AggregateReadResult.Project(snap.Suppressed, snap.SuppressionReason, () => Figures(snap));
    }

    /// <summary>Cross-cycle trend (FR-017): every CLOSED cycle of the same framework version, oldest first,
    /// with that cycle's node figures projected through its frozen verdict. The current OPEN cycle is not part
    /// of the trend history — the caller already holds its live figure in <see cref="NodeAggregateView.Current"/>
    /// and appends it as the latest point. Reads snapshots only, so every trend point reconciles.</summary>
    private async Task<IReadOnlyList<TrendPointView>> TrendAsync(
        Cycle current, OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct)
    {
        // Exclude the resolved "current" cycle: when there is no Open cycle, CurrentCycleAsync resolves the
        // most-recently-Closed cycle as current, whose figures are already the dashboard's Current — it must
        // not also appear as a trend point (red-team round-1 duplicate-current cleanup).
        var closedCycles = await _db.Cycles
            .Where(c => c.FrameworkVersionId == current.FrameworkVersionId
                        && c.State == CycleState.Closed
                        && c.Id != current.Id)
            .OrderBy(c => c.OpensAt)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        if (closedCycles.Count == 0)
        {
            return Array.Empty<TrendPointView>();
        }

        var cycleIds = closedCycles.Select(c => c.Id).ToList();
        var snaps = (await _db.RollupSnapshots.AsNoTracking()
                .Where(s => cycleIds.Contains(s.CycleId) && s.OrgNodeType == nodeType && s.OrgNodeRef == nodeRef)
                .ToListAsync(ct))
            .ToDictionary(s => s.CycleId);

        return closedCycles
            .Select(c =>
            {
                AggregateReadResult result = snaps.TryGetValue(c.Id, out var snap)
                    ? AggregateReadResult.Project(snap.Suppressed, snap.SuppressionReason, () => Figures(snap))
                    : new AggregateReadResult.Suppressed("N<4");
                return new TrendPointView(c.Id, c.Name, result);
            })
            .ToList();
    }

    private static AggregateFigures Figures(NodeRollup r) =>
        new(r.N, r.PerDimensionMean, r.FloorLevelDistribution, r.CompletionPct, r.UnmoderatedPct);

    private static AggregateFigures Figures(RollupSnapshot s) =>
        new(s.N, s.PerDimensionMean, s.FloorLevelDistribution, s.CompletionPct, s.UnmoderatedPct);

    /// <summary>Whether the addressed org node actually exists — the guard that turns an all-HIG caller's
    /// request for a nonexistent BU/group/portfolio into a 404 rather than a hollow suppressed 200. AllHig is
    /// the single null-ref node (always exists); a Team ref is the caller's own manager id (always exists).</summary>
    private async Task<bool> NodeExistsAsync(OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct) =>
        nodeType switch
        {
            OrgNodeType.Bu => nodeRef is Guid bu && await _db.BusinessUnits.AnyAsync(b => b.Id == bu, ct),
            OrgNodeType.Group => nodeRef is Guid g && await _db.Groups.AnyAsync(x => x.Id == g, ct),
            OrgNodeType.Portfolio => nodeRef is Guid p && await _db.Portfolios.AnyAsync(x => x.Id == p, ct),
            OrgNodeType.Team => true,
            OrgNodeType.AllHig => true,
            _ => false,
        };

    private async Task<IReadOnlyList<DimensionMetaView>> DimensionsAsync(Guid frameworkVersionId, CancellationToken ct) =>
        await _db.Dimensions
            .Where(d => d.FrameworkVersionId == frameworkVersionId)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => new DimensionMetaView(d.Key, d.Name, d.DisplayOrder))
            .ToListAsync(ct);

    private async Task<string> NodeNameAsync(OrgNodeType nodeType, Guid? nodeRef, CancellationToken ct) =>
        nodeType switch
        {
            OrgNodeType.Bu => await _db.BusinessUnits.Where(b => b.Id == nodeRef).Select(b => b.Name).SingleOrDefaultAsync(ct) ?? "",
            OrgNodeType.Group => await _db.Groups.Where(g => g.Id == nodeRef).Select(g => g.Name).SingleOrDefaultAsync(ct) ?? "",
            OrgNodeType.Portfolio => await _db.Portfolios.Where(p => p.Id == nodeRef).Select(p => p.Name).SingleOrDefaultAsync(ct) ?? "",
            OrgNodeType.Team => await _db.People.Where(p => p.Id == nodeRef).Select(p => p.DisplayName).SingleOrDefaultAsync(ct) ?? "",
            OrgNodeType.AllHig => "All HIG",
            _ => "",
        };

    /// <summary>The set of nodes a caller may see aggregates for (Q-024 aggregate scope). <see cref="AllHig"/>
    /// short-circuits every containment check (HIG Executive). Groups/portfolios in scope also expand to their
    /// BUs at build time, so a BU-addressed request within a leader's span is authorised.</summary>
    private sealed record AggregateScope(
        bool AllHig,
        IReadOnlySet<Guid> BuIds,
        IReadOnlySet<Guid> GroupIds,
        IReadOnlySet<Guid> PortfolioIds)
    {
        public static AggregateScope Everything() =>
            new(true, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        public bool CanSeeBu(Guid buId) => AllHig || BuIds.Contains(buId);
        public bool CanSeeGroup(Guid groupId) => AllHig || GroupIds.Contains(groupId);
        public bool CanSeePortfolio(Guid portfolioId) => AllHig || PortfolioIds.Contains(portfolioId);
    }
}
