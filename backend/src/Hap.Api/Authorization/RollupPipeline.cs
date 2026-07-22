using Hap.Domain.Assessments;
using Hap.Domain.Cycles;
using Hap.Domain.Rollups;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hap.Api.Authorization;

/// <summary>
/// The single definition of "how a cycle's moderated scores become per-node rollups" (FR-013/015/016/019,
/// research D2/D4). It lives IN the visibility seam because <see cref="BuildPersonInputsAsync"/> reads the
/// moderated <see cref="AssessmentScore"/> rows, which no code outside <c>Hap.Api/Authorization</c> may touch
/// (research D1).
///
/// <para><b>Why one pipeline, two entry points (HAP-11).</b> The close path (<see cref="CycleCloseProcessor"/>)
/// and the live open-cycle dashboard (<see cref="RollupReads"/>) MUST agree figure-for-figure on a just-closed
/// cycle. They do so <i>by construction</i>: both build person inputs here and compute nodes here — there is no
/// second implementation to drift. The ONLY difference is that the close path auto-adopts
/// (<c>Submitted → AutoAdopted</c>, FR-068) BEFORE calling <see cref="BuildPersonInputsAsync"/>, so the scored
/// population then includes auto-adopted rows; the live path calls it on an Open cycle where nothing has been
/// auto-adopted, so only genuinely <c>Moderated</c> rows contribute a score of record. The membership test —
/// state is <c>Moderated</c> or <c>AutoAdopted</c> with a populated manager score — is identical either way.</para>
///
/// <para>Uses tracking queries deliberately: on the close path this method is invoked AFTER auto-adoption has
/// mutated the tracked assessment/score entities but BEFORE <c>SaveChanges</c>, so the re-read must resolve to
/// those tracked (mutated) instances via EF identity resolution, not a stale database read.</para>
/// </summary>
public sealed class RollupPipeline
{
    private readonly SuppressionEvaluator _suppression;
    private readonly ILogger<RollupPipeline> _logger;

    public RollupPipeline(SuppressionEvaluator suppression, ILogger<RollupPipeline> logger)
    {
        _suppression = suppression;
        _logger = logger;
    }

    /// <summary>
    /// Build one <see cref="PersonRollupInput"/> per person in the participation universe (invited ∪ has-a-score)
    /// for <paramref name="cycle"/> — the scored population vs completion base split (FR-024/§3.5). A person
    /// contributes a score set only when their assessment is <c>Moderated</c> or <c>AutoAdopted</c> and its
    /// per-dimension manager scores are populated (the score of record). On an Open cycle no assessment is
    /// AutoAdopted yet, so this naturally admits only the already-moderated population — the "live via seam"
    /// reading (FR-013 "rollups from moderated scores").
    /// </summary>
    /// <param name="tracked">The CLOSE path passes <c>true</c>: it auto-adopts (mutating tracked assessment/
    /// score entities) BEFORE this re-reads them, so the re-read MUST resolve to those tracked instances via
    /// EF identity resolution. The LIVE dashboard path passes <c>false</c> — it never mutates, so
    /// <c>AsNoTracking</c> is both correct and cheaper (SC-008).</param>
    public async Task<IReadOnlyDictionary<Guid, PersonRollupInput>> BuildPersonInputsAsync(
        HapDbContext db, OrgGraph graph, Cycle cycle, bool tracked = true, CancellationToken ct = default)
    {
        var assessmentQuery = db.Set<Assessment>().Where(a => a.CycleId == cycle.Id);
        if (!tracked)
        {
            assessmentQuery = assessmentQuery.AsNoTracking();
        }
        var assessments = await assessmentQuery.ToListAsync(ct);
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var scoreQuery = db.Set<AssessmentScore>().Where(s => assessmentIds.Contains(s.AssessmentId));
        if (!tracked)
        {
            scoreQuery = scoreQuery.AsNoTracking();
        }
        var scores = await scoreQuery.ToListAsync(ct);
        var scoresByAssessment = scores.GroupBy(s => s.AssessmentId).ToDictionary(g => g.Key, g => g.ToList());

        // dimension id → key for THIS cycle's framework version (data, never hard-coded)
        var dimensionKeyById = await db.Dimensions
            .Where(d => d.FrameworkVersionId == cycle.FrameworkVersionId)
            .ToDictionaryAsync(d => d.Id, d => d.Key, ct);

        // invited, non-excluded people = the completion base BEFORE the active-at-close filter (FR-002/003)
        var invitedNonExcluded = (await db.CycleInvitations
                .Where(i => i.CycleId == cycle.Id && !i.Excluded)
                .Select(i => i.PersonId)
                .ToListAsync(ct))
            .ToHashSet();

        var assessmentByPerson = assessments.ToDictionary(a => a.PersonId);
        var universe = new HashSet<Guid>(invitedNonExcluded);
        universe.UnionWith(assessments.Select(a => a.PersonId));

        var personInputs = new Dictionary<Guid, PersonRollupInput>();
        foreach (var personId in universe)
        {
            var person = graph.Find(personId);
            if (person is null)
            {
                // People are never deleted (FR-024), so an id in the universe (invited ∪ has-a-score) the org
                // graph cannot resolve is a graph/universe desync — a scored person would be dropped from every
                // aggregate. Defensively skip rather than crash, but never silently.
                _logger.LogWarning(
                    "Cycle {CycleId}: person {PersonId} is in the participation universe but absent from the org graph — excluded from all rollup aggregates.",
                    cycle.Id, personId);
                continue;
            }

            IReadOnlyList<DimensionDatum>? scoreSet = null;
            var autoAdopted = false;
            if (assessmentByPerson.TryGetValue(personId, out var assessment)
                && assessment.State is AssessmentState.Moderated or AssessmentState.AutoAdopted
                && scoresByAssessment.TryGetValue(assessment.Id, out var rows))
            {
                // Score of record = ManagerScore (moderated, or self copied on auto-adopt).
                scoreSet = rows
                    .Where(r => r.ManagerScore is not null && dimensionKeyById.ContainsKey(r.DimensionId))
                    .Select(r => new DimensionDatum(dimensionKeyById[r.DimensionId], r.SelfScore, r.ManagerScore!.Value))
                    .ToList();
                autoAdopted = assessment.State == AssessmentState.AutoAdopted;
            }

            // Completion base: invited, non-excluded AND still active (leaver excluded, FR-024).
            var countsTowardCompletion = invitedNonExcluded.Contains(personId) && person.IsActive;
            personInputs[personId] = new PersonRollupInput(personId, countsTowardCompletion, scoreSet, autoAdopted);
        }

        return personInputs;
    }

    /// <summary>
    /// Compute every fixed-hierarchy node (Team / BU / Group / Portfolio / AllHig) from the person inputs:
    /// assign people to nodes, compute each node's numeric body via <see cref="RollupComputation"/>, freeze the
    /// per-parent FR-014 verdict by REUSING the seam's <see cref="SuppressionEvaluator"/> (research D2 — never
    /// re-implemented), THEN close the differencing defence hierarchy-globally via
    /// <see cref="HierarchySuppression"/> so no suppressed node is recoverable by summing published nodes at
    /// other levels (HAP-11 BR1 — the sacred cross-level guarantee). Pure over its inputs; no storage.
    /// </summary>
    public IReadOnlyList<NodeRollupResult> ComputeNodes(
        OrgGraph graph, IReadOnlyDictionary<Guid, PersonRollupInput> personInputs)
    {
        var nodes = BuildNodes(graph, personInputs.Values);

        foreach (var node in nodes.Values)
        {
            node.Rollup = RollupComputation.Compute(node.Members);
        }

        ApplySuppression(nodes);
        ApplyCrossLevelSuppression(nodes);

        return nodes.Values
            .Select(n => new NodeRollupResult(n.Type, n.Ref, n.Rollup!, n.Suppressed, n.SuppressionReason))
            .ToList();
    }

    /// <summary>Closes the differencing defence across the whole tree (HAP-11 BR1): maps the node graph onto
    /// <see cref="HierarchySuppression.Close"/> and suppresses any node it flags. A node newly suppressed here
    /// was recoverable by cross-level differencing, so its reason is <see cref="SuppressionReason.ComplementIdentifiable"/>
    /// (already-suppressed nodes keep their reason). The per-parent pass ran first, so this only ever ADDS
    /// suppression — it never un-suppresses.</summary>
    private static void ApplyCrossLevelSuppression(Dictionary<NodeKey, NodeAgg> nodes)
    {
        var list = nodes.Values.ToList();
        var n = list.Count;
        if (n == 0)
        {
            return;
        }

        var index = new Dictionary<NodeKey, int>(n);
        for (var i = 0; i < n; i++)
        {
            index[new NodeKey(list[i].Type, list[i].Ref)] = i;
        }

        var parent = new int[n];
        var count = new long[n];
        var suppressed = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var node = list[i];
            count[i] = node.Rollup!.N;
            suppressed[i] = node.Suppressed;
            parent[i] = node.ParentKey is NodeKey pk && index.TryGetValue(pk, out var pi) ? pi : -1;
        }

        var closed = HierarchySuppression.Close(n, parent, count, suppressed);
        for (var i = 0; i < n; i++)
        {
            if (closed[i] && !list[i].Suppressed)
            {
                list[i].Suppressed = true;
                list[i].SuppressionReason ??= ReasonText(SuppressionReason.ComplementIdentifiable);
            }
        }
    }

    /// <summary>Places every universe person into their BU, Group, Portfolio, and the AllHig node (via their
    /// HOME BU), and — per the B1/Q-023 team-membership rule — into a Team node only when their manager shares
    /// that home BU. A manager-less or cross-BU-managed person is teamless: counted at BU and above, in no Team.
    /// So every Team nests within exactly one BU and the suppression precondition (Σchild ≤ parent) holds.</summary>
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

            // Team membership (Q-023): a Team node exists only where the report and their manager share a HOME
            // BU. Manager-less (BU heads) or cross-BU-managed people are teamless — counted only at BU and above
            // via their home BU. Guarantees every Team nests within exactly one BU (Σ(team n) ≤ BU n).
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

    /// <summary>A team's parent BU is the home BU of its members (all share it, by the Q-023 rule).</summary>
    private static NodeKey ParentBuOfTeam(OrgGraph graph, List<PersonRollupInput> members)
    {
        var first = members.Select(m => graph.Find(m.PersonId)).FirstOrDefault(p => p is not null);
        return first is null ? new NodeKey(OrgNodeType.AllHig, null) : new NodeKey(OrgNodeType.Bu, first.BusinessUnitId);
    }

    /// <summary>Freezes each node's suppression verdict (FR-014/FR-071, research D2). AllHig has no parent so
    /// only rule 1 (n&lt;4) applies; every other node is evaluated as one of its parent's children via the seam's
    /// set-based <see cref="SuppressionEvaluator.EvaluateLevel"/> (which also catches multi-child differencing).</summary>
    private void ApplySuppression(Dictionary<NodeKey, NodeAgg> nodes)
    {
        if (nodes.TryGetValue(new NodeKey(OrgNodeType.AllHig, null), out var allHig))
        {
            var below = allHig.Rollup!.N < SuppressionEvaluator.MinGroupSize;
            allHig.Suppressed = below;
            allHig.SuppressionReason = below ? ReasonText(SuppressionReason.BelowThreshold) : null;
        }

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

    // A stable per-node id for suppression keying: the node's ref (unique within a parent level). AllHig is
    // never a child, so its null ref never needs an id here.
    private static Guid NodeIdOf(NodeAgg node) => node.Ref ?? Guid.Empty;

    internal static string ReasonText(SuppressionReason reason) => reason switch
    {
        SuppressionReason.BelowThreshold => "N<4",
        SuppressionReason.ComplementIdentifiable => "Complement",
        _ => reason.ToString(),
    };

    /// <summary>Identity of a fixed-hierarchy node: its type plus its ref (manager id / BU / group / portfolio
    /// id, or null for AllHig).</summary>
    public readonly record struct NodeKey(OrgNodeType Type, Guid? Ref);

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

/// <summary>One node's frozen rollup: its type/ref, the numeric body (<see cref="NodeRollup"/>), and the FR-014
/// suppression verdict. The verdict travels beside the figures so the read projection can decide published vs
/// suppressed WITHOUT the figures ever being read for a suppressed node (F2 / FR-071).</summary>
public sealed record NodeRollupResult(
    OrgNodeType Type, Guid? Ref, NodeRollup Rollup, bool Suppressed, string? SuppressionReason);
