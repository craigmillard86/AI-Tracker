using Hap.Domain.Assessments;
using Hap.Domain.Rollups;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hap.Api.Authorization;

/// <summary>
/// The seam-resident implementation of <see cref="ICycleCloseProcessor"/> (HAP-10). It is IN the
/// visibility seam because it reads moderated maturity scores, which no code outside
/// <c>Hap.Api/Authorization</c> may touch (research D1). Hooked into <see cref="CycleService.CloseAsync"/>
/// and run inside that method's transaction — it stages every write on the shared <see cref="HapDbContext"/>
/// and never opens its own transaction or commits.
///
/// <para>In one pass it: (1) auto-adopts every Submitted-but-unmoderated assessment — the self score
/// becomes the score of record, flagged unmoderated (FR-068), Moderated ones untouched, Q-020 handled
/// with no senior-leader special case; (2) computes a <see cref="RollupSnapshot"/> for every Team/BU/
/// Group/Portfolio/AllHig node with the two populations tracked apart (scored population vs completion
/// base, FR-024/§3.5); (3) freezes the FR-014 suppression verdict per node by reusing the seam's
/// <see cref="SuppressionEvaluator"/> — never re-implementing it — over the fixed hierarchy (research D2).</para>
/// </summary>
public sealed class CycleCloseProcessor : ICycleCloseProcessor
{
    private readonly HapDbContext _db;
    private readonly OrgGraphLoader _graphLoader;
    private readonly SuppressionEvaluator _suppression;
    private readonly ILogger<CycleCloseProcessor> _logger;

    public CycleCloseProcessor(
        HapDbContext db, OrgGraphLoader graphLoader, SuppressionEvaluator suppression, ILogger<CycleCloseProcessor> logger)
    {
        _db = db;
        _graphLoader = graphLoader;
        _suppression = suppression;
        _logger = logger;
    }

    public async Task RunAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        var cycle = await _db.Cycles.FindAsync(new object[] { cycleId }, cancellationToken)
            ?? throw new InvalidOperationException($"Cycle {cycleId} not found during close processing.");

        // --- load everything this close needs (assessments + scores are seam-only DbSets) --------------
        var assessments = await _db.Set<Assessment>()
            .Where(a => a.CycleId == cycleId)
            .ToListAsync(cancellationToken); // tracked — auto-adopt mutates these
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var scores = await _db.Set<AssessmentScore>()
            .Where(s => assessmentIds.Contains(s.AssessmentId))
            .ToListAsync(cancellationToken); // tracked — AdoptSelf mutates these
        var scoresByAssessment = scores.GroupBy(s => s.AssessmentId).ToDictionary(g => g.Key, g => g.ToList());

        // --- (1) auto-adoption: Submitted-but-unmoderated → AutoAdopted, self copied to the score of record
        foreach (var assessment in assessments.Where(a => a.State == AssessmentState.Submitted))
        {
            if (scoresByAssessment.TryGetValue(assessment.Id, out var rows))
            {
                foreach (var row in rows)
                {
                    row.AdoptSelf(); // manager := self, no comment (Δ0) — FR-068
                }
            }
            assessment.AutoAdopt(); // Submitted → AutoAdopted, unmoderated = true
        }

        // dimension id → key for THIS cycle's framework version (data, never hard-coded)
        var dimensionKeyById = await _db.Dimensions
            .Where(d => d.FrameworkVersionId == cycle.FrameworkVersionId)
            .ToDictionaryAsync(d => d.Id, d => d.Key, cancellationToken);

        // invited, non-excluded people = the completion base BEFORE the active-at-close filter (FR-002/003)
        var invitedNonExcluded = (await _db.CycleInvitations
                .Where(i => i.CycleId == cycleId && !i.Excluded)
                .Select(i => i.PersonId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var graph = await _graphLoader.LoadAsync(cancellationToken);

        // --- (2) build one PersonRollupInput per person in the universe (invited ∪ has-a-score) --------
        var assessmentByPerson = assessments.ToDictionary(a => a.PersonId);
        var universe = new HashSet<Guid>(invitedNonExcluded);
        universe.UnionWith(assessments.Select(a => a.PersonId));

        var personInputs = new Dictionary<Guid, PersonRollupInput>();
        foreach (var personId in universe)
        {
            var person = graph.Find(personId);
            if (person is null)
            {
                // People are never deleted (FR-024), so an id in the universe (invited ∪ has-a-score) that
                // the org graph cannot resolve is a graph/universe desync — a scored person would be dropped
                // from every aggregate. Defensively skip rather than crash the close, but never silently:
                // log so the desync is observable and can be chased (SHOULD-FIX, round-1 code review).
                _logger.LogWarning(
                    "Cycle close {CycleId}: person {PersonId} is in the participation universe but absent from the org graph — excluded from all rollup aggregates.",
                    cycleId, personId);
                continue;
            }

            IReadOnlyList<DimensionDatum>? scoreSet = null;
            var autoAdopted = false;
            if (assessmentByPerson.TryGetValue(personId, out var assessment)
                && assessment.State is AssessmentState.Moderated or AssessmentState.AutoAdopted
                && scoresByAssessment.TryGetValue(assessment.Id, out var rows))
            {
                // Score of record = ManagerScore (moderated, or self copied on auto-adopt above).
                scoreSet = rows
                    .Where(r => r.ManagerScore is not null && dimensionKeyById.ContainsKey(r.DimensionId))
                    .Select(r => new DimensionDatum(dimensionKeyById[r.DimensionId], r.SelfScore, r.ManagerScore!.Value))
                    .ToList();
                autoAdopted = assessment.State == AssessmentState.AutoAdopted;
            }

            // Completion base membership: invited, non-excluded AND still active at close (leaver excluded).
            var countsTowardCompletion = invitedNonExcluded.Contains(personId) && person.IsActive;
            personInputs[personId] = new PersonRollupInput(personId, countsTowardCompletion, scoreSet, autoAdopted);
        }

        // --- assign people to the fixed hierarchy nodes ------------------------------------------------
        var nodes = BuildNodes(graph, personInputs.Values);

        // --- compute the numeric body of each node -----------------------------------------------------
        foreach (var node in nodes.Values)
        {
            node.Rollup = RollupComputation.Compute(node.Members);
        }

        // --- (3) freeze the suppression verdict per node over the tree (reusing the seam evaluator) -----
        ApplySuppression(nodes);

        // --- persist snapshots (append-only; CloseAsync commits) ---------------------------------------
        foreach (var node in nodes.Values)
        {
            var r = node.Rollup!;
            _db.RollupSnapshots.Add(RollupSnapshot.Create(
                cycleId,
                node.Type,
                node.Ref,
                r.N,
                r.PerDimensionMean,
                r.FloorLevelDistribution,
                r.CompletionDenominator,
                r.CompletionPct,
                r.UnmoderatedPct,
                r.CalibrationDelta,
                node.Suppressed,
                node.SuppressionReason));
        }
    }

    /// <summary>Places every universe person into their BU, Group, Portfolio, and the AllHig node (via their
    /// HOME BU), and — per the B1/Q-023 team-membership rule — into a Team node only when their manager shares
    /// that home BU. A manager-less or cross-BU-managed person is teamless: counted at BU and above, in no
    /// Team. So every Team nests within exactly one BU, and the scored-population reconciliation is the
    /// team-homed carve-out — Σ(team-homed scored in a BU) = the BU's team-homed scored n, NOT its whole n
    /// (teamless people are the difference). Keyed so each level's nodes are distinct.</summary>
    private static Dictionary<NodeKey, NodeAgg> BuildNodes(OrgGraph graph, IEnumerable<PersonRollupInput> people)
    {
        var nodes = new Dictionary<NodeKey, NodeAgg>();

        NodeAgg NodeFor(OrgNodeType type, Guid? nodeRef)
        {
            var key = new NodeKey(type, nodeRef);
            if (!nodes.TryGetValue(key, out var node))
            {
                node = new NodeAgg(type, nodeRef);
                nodes[key] = node;
            }
            return node;
        }

        foreach (var person in people)
        {
            var org = graph.Find(person.PersonId);
            if (org is null)
            {
                continue;
            }

            var buId = org.BusinessUnitId;
            NodeFor(OrgNodeType.AllHig, null).Members.Add(person);
            NodeFor(OrgNodeType.Bu, buId).Members.Add(person);

            // Team membership (Q-023, domain ruling B1): a Team node exists only where the report and their
            // manager share a HOME BU. A manager-less person (a BU head) or a cross-BU-managed person (home
            // BU differs from the manager's — synth edge HAP-EDGE-XBU-REPORT, or an admin OverrideField.Manager)
            // is TEAMLESS: counted only at BU/Group/Portfolio/AllHig via their home BU, in no Team node. This
            // guarantees every Team nests within exactly one BU, so Σ(team n) ≤ BU n always holds — the
            // suppression evaluator never sees children summing past their parent (no throw, no wrong verdict),
            // and the reconciliation invariant is well-defined. Their manager still REVIEWS them via the
            // existing DR-0006/FR-070 reviewer-of-record path; this only shapes the aggregate rollup node.
            if (org.ManagerPersonId is Guid managerId
                && graph.Find(managerId) is { BusinessUnitId: var managerBuId }
                && managerBuId == buId)
            {
                NodeFor(OrgNodeType.Team, managerId).Members.Add(person);
            }

            if (graph.GroupOfBu(buId) is Guid groupId)
            {
                NodeFor(OrgNodeType.Group, groupId).Members.Add(person);
                if (graph.PortfolioOfGroup(groupId) is Guid portfolioId)
                {
                    NodeFor(OrgNodeType.Portfolio, portfolioId).Members.Add(person);
                }
            }
        }

        // Record each node's parent so the suppression pass can evaluate a parent's children as a set.
        foreach (var node in nodes.Values)
        {
            node.ParentKey = node.Type switch
            {
                OrgNodeType.Team => ParentBuOfTeam(graph, node.Members),
                OrgNodeType.Bu => graph.GroupOfBu(node.Ref!.Value) is Guid g ? new NodeKey(OrgNodeType.Group, g) : new NodeKey(OrgNodeType.AllHig, null),
                OrgNodeType.Group => graph.PortfolioOfGroup(node.Ref!.Value) is Guid p ? new NodeKey(OrgNodeType.Portfolio, p) : new NodeKey(OrgNodeType.AllHig, null),
                OrgNodeType.Portfolio => new NodeKey(OrgNodeType.AllHig, null),
                OrgNodeType.AllHig => null,
                _ => null,
            };
        }

        return nodes;
    }

    /// <summary>A team's parent BU is the home BU of its members. By the B1/Q-023 team-membership rule every
    /// team member shares one home BU (the manager's), so any member resolves it. Falls back to AllHig if
    /// somehow empty.</summary>
    private static NodeKey ParentBuOfTeam(OrgGraph graph, List<PersonRollupInput> members)
    {
        var first = members.Select(m => graph.Find(m.PersonId)).FirstOrDefault(p => p is not null);
        return first is null ? new NodeKey(OrgNodeType.AllHig, null) : new NodeKey(OrgNodeType.Bu, first.BusinessUnitId);
    }

    /// <summary>Freezes each node's suppression verdict (FR-014/FR-071, research D2). The single top node
    /// (AllHig) has no parent, so only rule 1 (n&lt;4) applies. Every other node gets its verdict from being
    /// evaluated as one of its parent's children via the seam's <see cref="SuppressionEvaluator"/> — the
    /// set-based publication path that also catches multi-child differencing. Never re-implements the rule.</summary>
    private void ApplySuppression(Dictionary<NodeKey, NodeAgg> nodes)
    {
        // AllHig: rule 1 only.
        if (nodes.TryGetValue(new NodeKey(OrgNodeType.AllHig, null), out var allHig))
        {
            var below = allHig.Rollup!.N < SuppressionEvaluator.MinGroupSize;
            allHig.Suppressed = below;
            allHig.SuppressionReason = below ? ReasonText(SuppressionReason.BelowThreshold) : null;
        }

        // Every other node: evaluate each parent's children together.
        var childrenByParent = nodes.Values
            .Where(n => n.ParentKey is not null)
            .GroupBy(n => n.ParentKey!.Value);

        foreach (var group in childrenByParent)
        {
            var parent = nodes[group.Key];
            var children = group.ToList();
            var inputs = children
                .Select(c => new AggregateInput(NodeIdOf(c), c.Rollup!.N, 0d))
                .ToList();

            var outcomes = _suppression.EvaluateLevel(parent.Rollup!.N, inputs);
            foreach (var child in children)
            {
                var outcome = outcomes[NodeIdOf(child)];
                if (outcome is AggregateOutcome.Suppressed s)
                {
                    child.Suppressed = true;
                    child.SuppressionReason = ReasonText(s.Reason);
                }
                else
                {
                    child.Suppressed = false;
                    child.SuppressionReason = null;
                }
            }
        }
    }

    // A stable per-node id for suppression keying: the node's ref (unique within a parent level). AllHig
    // is never a child, so its null ref never needs an id here.
    private static Guid NodeIdOf(NodeAgg node) => node.Ref ?? Guid.Empty;

    private static string ReasonText(SuppressionReason reason) => reason switch
    {
        SuppressionReason.BelowThreshold => "N<4",
        SuppressionReason.ComplementIdentifiable => "Complement",
        _ => reason.ToString(),
    };

    private readonly record struct NodeKey(OrgNodeType Type, Guid? Ref);

    private sealed class NodeAgg
    {
        public NodeAgg(OrgNodeType type, Guid? nodeRef)
        {
            Type = type;
            Ref = nodeRef;
        }

        public OrgNodeType Type { get; }
        public Guid? Ref { get; }
        public List<PersonRollupInput> Members { get; } = new();
        public NodeKey? ParentKey { get; set; }
        public NodeRollup? Rollup { get; set; }
        public bool Suppressed { get; set; }
        public string? SuppressionReason { get; set; }
    }
}
