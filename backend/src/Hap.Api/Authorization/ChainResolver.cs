using Hap.Domain.Org;

namespace Hap.Api.Authorization;

/// <summary>
/// Resolves management chains for the visibility seam (FR-025). The chain — not any role label — is
/// the individual-score read grant: a caller may read a subject's individual scores iff the caller is
/// the subject, or a non-excluded, active ancestor on the subject's upward management chain.
///
/// <para><b>Cycle-safe by construction.</b> Every walk carries a visited-set (guarantees termination)
/// AND a depth cap (<see cref="SeamOptions.MaxChainDepth"/>, bounds work). The seam NEVER assumes the
/// chain it walks is acyclic: a multi-node cycle (A.mgr=B, B.mgr=A) is importable through a future
/// non-synthetic directory adapter, so this read-side backstop must terminate and grant nothing it
/// should not on cyclic data (HAP-3 red-team carry-forward, AC "cycle-safe chain walk").</para>
///
/// <para><b>Contractor managers (Q-006, Restrictive default).</b> A contractor ancestor is EXCLUDED
/// from the individual-read grant, but the walk still passes THROUGH them so their own (employee)
/// managers keep access; the contractor's reports' reviews escalate past them
/// (<see cref="ReviewerOfRecord"/>/<see cref="EscalationManager"/>).</para>
///
/// <para><b>FR-025 clause split (QUESTIONS.md Q-015).</b> This grant implements clause 1 (the chain).
/// Clause 2 ("leaders above BU see aggregates only") is enforced at the role-scope layer
/// (<see cref="RoleScope"/> gives above-BU roles no individual-read capability); capping an in-chain
/// above-BU leader at the BU-Lead tier needs the Q-014 structural anchor and is deferred.</para>
/// </summary>
public sealed class ChainResolver
{
    private readonly SeamOptions _options;

    public ChainResolver(SeamOptions options) => _options = options;

    /// <summary>
    /// The subject's ancestors, nearest-first (direct manager, then their manager, …), up to the top
    /// of the chain or the depth cap. Only real, loaded people are included. The walk ends on a
    /// manager gap (null manager, FR-069), a dangling manager reference (id absent from the graph), a
    /// revisited node (cycle), or the depth cap. Ancestors are included regardless of their own active
    /// flag — a departed manager's edge still defines who sits above (reassignment is
    /// <see cref="ReviewerOfRecord"/>'s job, FR-070) — so a caller-access check must still confirm the
    /// ancestor is active and non-excluded (see <see cref="GrantsIndividualRead"/>).
    /// </summary>
    public IReadOnlyList<Guid> UpwardChain(OrgGraph graph, Guid subjectId)
    {
        var chain = new List<Guid>();
        var visited = new HashSet<Guid> { subjectId };
        Guid? current = graph.Find(subjectId)?.ManagerPersonId;
        int guard = 0;

        while (current is not null && guard < _options.MaxChainDepth)
        {
            guard++;
            if (!visited.Add(current.Value))
            {
                break; // cycle guard: a node already on the walk means the chain loops — stop safely.
            }
            var node = graph.Find(current.Value);
            if (node is null)
            {
                break; // manager reference points outside the loaded population — chain ends.
            }
            chain.Add(current.Value);
            current = node.ManagerPersonId;
        }

        return chain;
    }

    /// <summary>
    /// May <paramref name="callerId"/> read <paramref name="subjectId"/>'s individual scores? True iff
    /// caller == subject (own data), or caller is an ACTIVE, non-excluded ancestor on the subject's
    /// upward chain. A contractor ancestor is excluded under the Restrictive policy (Q-006). This is
    /// the whole individual-read grant — no role path adds to it (FR-025 clause 1; SC-005 "zero reads
    /// outside the management chain").
    /// </summary>
    public bool GrantsIndividualRead(OrgGraph graph, Guid callerId, Guid subjectId)
    {
        if (callerId == subjectId)
        {
            return true; // own data
        }

        foreach (var ancestorId in UpwardChain(graph, subjectId))
        {
            if (ancestorId != callerId)
            {
                continue;
            }
            var ancestor = graph.Find(ancestorId);
            return ancestor is not null && ancestor.IsActive && !IsExcludedManager(ancestor);
        }

        return false;
    }

    /// <summary>
    /// The person responsible for moderating the subject's assessment (reviewer of record). Normally
    /// the direct manager; if that manager is a gap (null/dangling), inactive (departed mid-cycle,
    /// FR-070), or an excluded contractor (Q-006), responsibility escalates up the chain to the first
    /// ACTIVE, non-excluded ancestor. Null if the chain yields no such person (a fully detached
    /// subject) — the caller decides what to do with an unassignable review.
    /// </summary>
    public Guid? ReviewerOfRecord(OrgGraph graph, Guid subjectId)
    {
        foreach (var ancestorId in UpwardChain(graph, subjectId))
        {
            var ancestor = graph.Find(ancestorId);
            if (ancestor is not null && ancestor.IsActive && !IsExcludedManager(ancestor))
            {
                return ancestorId;
            }
        }
        return null;
    }

    /// <summary>
    /// The escalation target ABOVE the subject's direct manager — the manager's manager (then upward),
    /// applying the same active/non-excluded rule. Used when the direct manager departs (FR-070) and
    /// the escalation must explicitly skip the direct-manager slot even if it is somehow still valid.
    /// Null if no eligible ancestor exists above the direct manager.
    /// </summary>
    public Guid? EscalationManager(OrgGraph graph, Guid subjectId)
    {
        var chain = UpwardChain(graph, subjectId);
        for (int i = 1; i < chain.Count; i++) // i = 0 is the direct manager; escalate above it.
        {
            var ancestor = graph.Find(chain[i]);
            if (ancestor is not null && ancestor.IsActive && !IsExcludedManager(ancestor))
            {
                return chain[i];
            }
        }
        return null;
    }

    private bool IsExcludedManager(OrgPerson person) =>
        _options.ContractorManagerPolicy == ContractorManagerPolicy.Restrictive
        && person.EmployeeType == EmployeeType.Contractor;
}
