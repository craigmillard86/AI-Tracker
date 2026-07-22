using Hap.Domain.Assessments;
using Hap.Domain.Org;

namespace Hap.Api.Authorization;

/// <summary>The outcome of an access decision — allowed, or denied with a reason for audit/diagnostics.
/// The reason NEVER contains score data.</summary>
public sealed record ReadDecision(bool Allowed, string Reason)
{
    public static readonly ReadDecision Allow = new(true, "");

    public static ReadDecision Deny(string reason) => new(false, reason);
}

/// <summary>One explicit role grant the caller holds, as re-read from the <c>RoleGrant</c> table
/// (HAP-4 A3: BU-scoped grants flatten to a bare role in the cookie, so the anchoring
/// <see cref="BusinessUnitId"/> must come from the database, never the claim). The caller-context
/// builder at the endpoint constructs these from a fresh DB read.</summary>
public sealed record CallerGrant(OrgRole Role, Guid? BusinessUnitId = null);

/// <summary>The caller as the seam sees them: their person id plus their explicit grants (structurally
/// re-read, never trusted from the cookie). Everything else about the caller's authority — whether they
/// are a line manager, which BU they lead — is derived STRUCTURALLY from these grants and the org graph,
/// never from <c>HierarchyRoleResolver</c>'s depth-derived tier labels (Q-014).</summary>
public sealed record CallerContext(Guid PersonId, IReadOnlyList<CallerGrant> Grants)
{
    public static CallerContext Ungranted(Guid personId) => new(personId, Array.Empty<CallerGrant>());
}

/// <summary>Thrown when a caller attempts to read individual assessment data they are not entitled to.
/// The gateway throws this BEFORE touching storage, so an unauthorised read never reaches the data.</summary>
public sealed class AssessmentAccessDeniedException : Exception
{
    public AssessmentAccessDeniedException(Guid callerId, Guid subjectPersonId, string reason)
        : base($"Caller {callerId} may not read individual assessment data for {subjectPersonId}: {reason}")
    {
        CallerId = callerId;
        SubjectPersonId = subjectPersonId;
    }

    public Guid CallerId { get; }
    public Guid SubjectPersonId { get; }
}

/// <summary>
/// The single gateway for reading assessment data (research D1): every individual-score read passes an
/// access decision here BEFORE any data is fetched, and this is the only type that touches assessment
/// storage (via <see cref="IAssessmentStore"/>).
///
/// <para><b>Two gates, both structural (L3 panel Q-015 ruling).</b> An individual read requires BOTH
/// (a) the reader's role — derived structurally — carries an individual-read capability
/// (<see cref="RoleScope"/>.<see cref="VisibilityScope.AllowsIndividualRead"/>), AND (b) the subject is
/// within that role's structural reach. This closes the org-wide over-grant the panel found: an above-BU
/// leader (Group/Portfolio/Exec) is a management-chain ancestor of ~everyone below them, but their role
/// has no individual-read capability, so they are DENIED even as a genuine ancestor (FR-025 clause 2).</para>
///
/// <para><b>Fail closed on the Q-014-bound residual.</b> A hierarchy BU Lead is, structurally, an
/// ancestor of people who may sit outside their true BU (the BU01-collapse problem, Q-014), and the
/// hierarchy tiers cannot be told apart without the unratified structural anchor. So this gateway grants
/// only what it can PROVE within-BU without depth labels: a caller reads a subject's individual scores
/// when the caller is the subject's DIRECT manager (spec: "Manager sees direct reports"), or holds an
/// explicit BU-scope grant (<see cref="OrgRole.BuDelegate"/>) covering the subject's BU. A transitive
/// (skip-level / BU-wide) read by a caller we cannot structurally attribute to a within-BU role FAILS
/// CLOSED — denied. The one-hop direct read (incl. an above-BU hierarchy leader reading their own direct
/// report) is <b>ratified per <see href="../../../docs/decisions/DR-0005-above-bu-direct-report-read.md">DR-0005</see></b>
/// (Q-015, owner-ratified ALLOW); the broad transitive cap remains deferred to Q-014. Because DR-0005
/// lifted the HAP-8 precondition, a live cross-person read endpoint is now wired on top of this gateway
/// (HAP-9 <c>GET /api/team/members/{id}/assessment</c>), which enforces exactly the ratified policy.</para>
/// </summary>
public sealed class AssessmentReads
{
    private readonly ChainResolver _chain;
    private readonly SeamOptions _options;
    private readonly IAssessmentStore _store;

    public AssessmentReads(ChainResolver chain, SeamOptions options, IAssessmentStore store)
    {
        _chain = chain;
        _options = options;
        _store = store;
    }

    /// <summary>The access decision for reading one subject's individual scores. Pure and
    /// side-effect-free, so it is exhaustively unit-testable and can also drive an audit row at the call
    /// site. Fails closed: anything not provably within-BU is denied.</summary>
    public ReadDecision AuthorizeIndividualRead(OrgGraph graph, CallerContext caller, Guid subjectPersonId)
    {
        if (caller.PersonId == subjectPersonId)
        {
            return ReadDecision.Allow; // own data
        }

        // Gate (a): does the reader's STRUCTURALLY-derived role carry individual-read capability at all?
        var (role, delegatedBusinessUnitId) = ClassifyReader(caller, graph);
        var (allowsIndividualRead, reach) = RoleScope.IndividualReadCapability(role);
        if (!allowsIndividualRead)
        {
            return ReadDecision.Deny(
                $"reader role {role} has no individual-read capability — above-BU leaders see aggregates only (FR-025)");
        }

        // The reader must themselves be eligible to hold individual data: active, and (Restrictive
        // default, Q-006) not a contractor — a contractor gets no individual-score access at all.
        if (!ReaderEligible(graph, caller.PersonId))
        {
            return ReadDecision.Deny("reader is inactive or an excluded contractor (Q-006)");
        }

        var subject = graph.Find(subjectPersonId);
        if (subject is null)
        {
            return ReadDecision.Deny("subject is not in the org graph");
        }

        bool isDirectManager = subject.ManagerPersonId == caller.PersonId;

        return reach switch
        {
            // Manager: the caller must be the subject's REVIEWER OF RECORD — the first active,
            // non-excluded manager up the chain. Normally that is the literal direct manager; where the
            // direct manager is a contractor (DR-0006) or departed/inactive (FR-070), responsibility
            // escalates to the first employee ancestor, who is the effective line manager and may read
            // (and moderate) the report. A transitive/skip-level read past a VALID employee manager is
            // NOT the reviewer of record → fail closed (broad BU-tier cap deferred to Q-014; one-hop
            // direct read ratified per DR-0005).
            IndividualReadReach.DirectReports =>
                _chain.ReviewerOfRecord(graph, subjectPersonId) == caller.PersonId
                    ? ReadDecision.Allow
                    : ReadDecision.Deny(
                        "only the subject's reviewer of record (first active, non-excluded manager, escalated past a contractor/departed direct manager per DR-0006/FR-070) reads as a manager — transitive/skip-level reads fail closed (Q-014)"),

            // BU delegate: a direct report in any BU (the chain rule governs across BU boundaries), OR
            // any subject homed in the explicitly-delegated BU.
            IndividualReadReach.BusinessUnit =>
                isDirectManager || (delegatedBusinessUnitId is Guid bu && subject.BusinessUnitId == bu)
                    ? ReadDecision.Allow
                    : ReadDecision.Deny("subject is outside the reader's delegated business unit"),

            _ => ReadDecision.Deny("reader has no individual-read reach"),
        };
    }

    /// <summary>
    /// Fetch a subject's individual scores — FAIL-CLOSED: an unauthorised caller gets an
    /// <see cref="AssessmentAccessDeniedException"/> and the store is NEVER queried (proven by the fake
    /// store's call count in unit tests). This is the read path every future assessment-read endpoint
    /// must funnel through; there is no other way to reach the scores.
    /// </summary>
    public async Task<IReadOnlyList<AssessmentScore>> ReadIndividualScoresAsync(
        OrgGraph graph, CallerContext caller, Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var decision = AuthorizeIndividualRead(graph, caller, subjectPersonId);
        if (!decision.Allowed)
        {
            throw new AssessmentAccessDeniedException(caller.PersonId, subjectPersonId, decision.Reason);
        }
        return await _store.GetIndividualScoresAsync(subjectPersonId, cycleId, cancellationToken);
    }

    /// <summary>
    /// The access decision for MODERATING a subject's assessment (HAP-9; FR-008/010). <b>Moderation ⊆
    /// read</b>: the caller must BOTH be able to read the subject's individual scores at all
    /// (<see cref="AuthorizeIndividualRead"/> — role capability + reach + reader eligibility) AND be the
    /// subject's REVIEWER OF RECORD — the first active, non-excluded manager up the chain
    /// (<see cref="ChainResolver.ReviewerOfRecord"/>): normally the literal direct manager, but escalated
    /// past a contractor (DR-0006) or departed/inactive (FR-070) one to the first employee ancestor.
    ///
    /// <para><b>The read gate is load-bearing, not decorative (L3 red-team).</b> Without it, a caller who
    /// is a chain ancestor but has NO individual-read capability — e.g. a HIG Executive who is the
    /// reviewer of record of a Portfolio Leader (FR-025 clause 2) — could reach the FR-009 forced-comment
    /// path, whose error text would then leak the subject's exact self-score as a probing oracle. Gating
    /// moderation on the same read decision closes that: a role denied the read is denied moderation.</para>
    ///
    /// <para>A BU delegate can READ BU-wide but only MODERATES their own reviewee-of-record (the second
    /// conjunct binds); the subject may not moderate themselves. Pure and side-effect-free — the endpoint
    /// funnels through here exactly as reads funnel through <see cref="AuthorizeIndividualRead"/>. A denied
    /// moderation surfaces as a 404 (existence-leak) with NO audit row, identical to a denied read.</para>
    /// </summary>
    public ReadDecision AuthorizeModeration(OrgGraph graph, CallerContext caller, Guid subjectPersonId)
    {
        if (caller.PersonId == subjectPersonId)
        {
            return ReadDecision.Deny("a person cannot moderate their own assessment");
        }

        var subject = graph.Find(subjectPersonId);
        if (subject is null)
        {
            return ReadDecision.Deny("subject is not in the org graph");
        }

        // Conjunct 1 — the caller must be able to READ the subject at all (capability + reach + reader
        // eligibility). This is what denies a capability-less chain ancestor (Exec/Group/Admin) and closes
        // the score-oracle leak. Fail closed with the read reason.
        var readDecision = AuthorizeIndividualRead(graph, caller, subjectPersonId);
        if (!readDecision.Allowed)
        {
            return readDecision;
        }

        // Conjunct 2 — and be the reviewer of record. Escalates past a contractor (DR-0006)/departed
        // (FR-070) direct manager to the first employee ancestor; denies the contractor itself, a
        // skip-level manager above the reviewer of record, and a BU delegate who is not the reviewer.
        return _chain.ReviewerOfRecord(graph, subjectPersonId) == caller.PersonId
            ? ReadDecision.Allow
            : ReadDecision.Deny(
                "only the subject's reviewer of record (first active, non-excluded manager, escalated past a contractor/departed direct manager per DR-0006/FR-070) may moderate");
    }

    /// <summary>
    /// Derive the reader's seam role STRUCTURALLY — explicit grants first (fail closed: any above-BU /
    /// group / admin grant strips individual-read capability), then a BU-scope delegate grant, then org
    /// position (has active direct reports ⇒ Manager, else Individual). NEVER consults
    /// <c>HierarchyRoleResolver</c>'s depth labels (Q-014): a reader we cannot prove is within-BU is
    /// treated as a plain Manager (direct-reports-only) or Individual, both of which fail closed on
    /// transitive reads rather than opening them.
    /// </summary>
    internal static (SeamRole Role, Guid? BusinessUnitId) ClassifyReader(CallerContext caller, OrgGraph graph)
    {
        if (caller.Grants.Any(g => g.Role == OrgRole.HigExecutive))
        {
            return (SeamRole.HigExecutive, null);
        }
        if (caller.Grants.Any(g => g.Role == OrgRole.PlatformAdmin))
        {
            return (SeamRole.PlatformAdmin, null);
        }
        if (caller.Grants.Any(g => g.Role == OrgRole.GroupViewer))
        {
            return (SeamRole.GroupLeader, null); // group-level read grant = aggregates only
        }

        var buDelegate = caller.Grants.FirstOrDefault(g => g.Role == OrgRole.BuDelegate && g.BusinessUnitId is not null);
        if (buDelegate is not null)
        {
            return (SeamRole.BuLead, buDelegate.BusinessUnitId);
        }

        return graph.HasDirectReports(caller.PersonId)
            ? (SeamRole.Manager, null)
            : (SeamRole.Individual, null);
    }

    private bool ReaderEligible(OrgGraph graph, Guid readerId)
    {
        var reader = graph.Find(readerId);
        if (reader is null || !reader.IsActive)
        {
            return false;
        }
        // A contractor gets no individual-score access at all under the Restrictive default (Q-006),
        // regardless of any grant — the safeguarding seam rounds up.
        return _options.ContractorManagerPolicy != ContractorManagerPolicy.Restrictive
            || reader.EmployeeType != EmployeeType.Contractor;
    }
}
