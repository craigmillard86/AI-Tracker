using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Domain.Org;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// The gateway's TWO structural gates (L3 panel Q-015 ruling): an individual read requires the reader's
/// structurally-derived role to carry individual-read capability (RoleScope.AllowsIndividualRead) AND the
/// subject to be within that role's reach. Closes the org-wide over-grant (above-BU chain ancestors) and
/// fails closed on the Q-014-bound transitive residual. A counting fake store proves fail-closed reads
/// never touch storage. Category=PrivacyReporting.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class AssessmentReadsGatewayTests
{
    private sealed class CountingStore : IAssessmentStore
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AssessmentScore>> GetIndividualScoresAsync(
            Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AssessmentScore>>(
                new[] { AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 2, null) });
        }

        // These gateway tests only reach the store via GetIndividualScoresAsync (counted above); the
        // moderation-path members are never exercised here and must fail loudly if reached.
        public Task<AssessmentWithScores?> GetAssessmentWithScoresAsync(
            Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssessmentWithScores?> GetByIdWithScoresAsync(
            Guid assessmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Assessment>> GetAssessmentsForPeopleAsync(
            Guid cycleId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetNonResponderPersonIdsAsync(
            Guid cycleId, IReadOnlyCollection<Guid> invitedPersonIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ModerateAsync(
            Guid assessmentId, Guid moderatedByPersonId, IReadOnlyList<ManagerScoreInput> decisions,
            Hap.Domain.Audit.AuditLog auditRow, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RetentionErasureResult> RunRetentionErasureAsync(
            IReadOnlyCollection<Guid> cycleIds, IReadOnlySet<Guid> alreadyErasedAssessmentIds,
            Func<Hap.Domain.Assessments.Assessment, Hap.Domain.Audit.AuditLog> auditFor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // evp → mgr → ind ; stranger under evp ; ctr (contractor) under evp → ctr_report ; xbu (BU2) under evp
    private static (OrgGraph graph, GraphBuilder b) Org()
    {
        var b = new GraphBuilder()
            .Bu("BU1", "G1", "P1").Bu("BU2", "G1", "P1")
            .Person("evp", "BU1")
            .Person("mgr", "BU1", manager: "evp")
            .Person("ind", "BU1", manager: "mgr")
            .Person("stranger", "BU1", manager: "evp")
            .Person("ctr", "BU1", manager: "evp", type: EmployeeType.Contractor)
            .Person("ctr_report", "BU1", manager: "ctr")
            .Person("xbu", "BU2", manager: "evp");
        return (b.Build(), b);
    }

    private static AssessmentReads Gateway(CountingStore store) =>
        new(new ChainResolver(SeamOptions.Default), SeamOptions.Default, store);

    private static CallerContext Ungranted(Guid id) => CallerContext.Ungranted(id);
    private static CallerContext WithGrant(Guid id, OrgRole role, Guid? bu = null) =>
        new(id, new[] { new CallerGrant(role, bu) });

    // --- self + direct-manager: the reads that must keep working ------------------------------------

    [Fact]
    public async Task Self_and_direct_manager_are_allowed_and_reach_the_store()
    {
        var (g, b) = Org();
        var store = new CountingStore();
        var gateway = Gateway(store);

        Assert.True(gateway.AuthorizeIndividualRead(g, Ungranted(b.Id("ind")), b.Id("ind")).Allowed);   // self
        var scores = await gateway.ReadIndividualScoresAsync(g, Ungranted(b.Id("mgr")), b.Id("ind"), Guid.NewGuid());
        Assert.Single(scores);
        Assert.Equal(1, store.Calls); // direct manager reached the store
    }

    [Fact]
    public void Direct_manager_across_a_BU_boundary_is_allowed_chain_rule_governs()
    {
        var (g, b) = Org();
        // evp directly manages xbu (homed in BU2). A direct-report read crosses the BU boundary.
        Assert.True(Gateway(new CountingStore()).AuthorizeIndividualRead(g, Ungranted(b.Id("evp")), b.Id("xbu")).Allowed);
    }

    // --- THE FIX: an above-BU chain ancestor is DENIED (was the panel's over-grant) -----------------

    [Fact]
    public async Task Transitive_ancestor_is_denied_and_the_store_is_never_queried()
    {
        var (g, b) = Org();
        var store = new CountingStore();
        var gateway = Gateway(store);

        // evp is a genuine chain ancestor of ind (evp → mgr → ind) but NOT ind's direct manager. An
        // ungranted transitive ancestor cannot be attributed to a within-BU role without Q-014 → denied.
        Assert.False(gateway.AuthorizeIndividualRead(g, Ungranted(b.Id("evp")), b.Id("ind")).Allowed);
        await Assert.ThrowsAsync<AssessmentAccessDeniedException>(() =>
            gateway.ReadIndividualScoresAsync(g, Ungranted(b.Id("evp")), b.Id("ind"), Guid.NewGuid()));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Above_BU_grants_have_no_individual_read_even_as_a_chain_ancestor()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());

        // A HIG Executive / Group Viewer grant strips individual-read capability (FR-025 clause 2), even
        // though the holder sits above ind in the chain.
        Assert.False(gateway.AuthorizeIndividualRead(g, WithGrant(b.Id("evp"), OrgRole.HigExecutive), b.Id("ind")).Allowed);
        Assert.False(gateway.AuthorizeIndividualRead(g, WithGrant(b.Id("evp"), OrgRole.GroupViewer), b.Id("ind")).Allowed);
        Assert.False(gateway.AuthorizeIndividualRead(g, WithGrant(b.Id("evp"), OrgRole.PlatformAdmin), b.Id("ind")).Allowed);
    }

    [Fact]
    public void A_person_with_no_reports_and_no_grant_cannot_read_anyone_else()
    {
        var (g, b) = Org();
        // stranger has no reports (Individual) → no individual-read capability over others.
        Assert.False(Gateway(new CountingStore())
            .AuthorizeIndividualRead(g, Ungranted(b.Id("stranger")), b.Id("ind")).Allowed);
    }

    // --- BuDelegate: the explicit within-BU grant that DOES enable BU-wide reads --------------------

    [Fact]
    public async Task Bu_delegate_reads_any_individual_in_the_delegated_bu_including_transitively()
    {
        var (g, b) = Org();
        var store = new CountingStore();
        var gateway = Gateway(store);
        var evpDelegate = WithGrant(b.Id("evp"), OrgRole.BuDelegate, b.Id("BU1"));

        // ind is homed in BU1 and is a transitive (not direct) report — the BuDelegate grant authorises it.
        var scores = await gateway.ReadIndividualScoresAsync(g, evpDelegate, b.Id("ind"), Guid.NewGuid());
        Assert.Single(scores);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public void Bu_delegate_cannot_read_a_non_report_outside_the_delegated_bu()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());
        // Delegate for BU2, reading ind (BU1) who is not their direct report → outside scope → denied.
        Assert.False(gateway.AuthorizeIndividualRead(g, WithGrant(b.Id("evp"), OrgRole.BuDelegate, b.Id("BU2")), b.Id("ind")).Allowed);
    }

    // --- contractor manager (Q-006): denied as a reader, store never touched -----------------------

    [Fact]
    public async Task Contractor_manager_is_denied_reading_their_own_report()
    {
        var (g, b) = Org();
        var store = new CountingStore();
        var gateway = Gateway(store);

        // ctr is ctr_report's DIRECT manager, but a contractor gets no individual-score access (restrictive).
        await Assert.ThrowsAsync<AssessmentAccessDeniedException>(() =>
            gateway.ReadIndividualScoresAsync(g, Ungranted(b.Id("ctr")), b.Id("ctr_report"), Guid.NewGuid()));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Bu_delegate_above_a_contractor_still_reads_the_contractors_report()
    {
        var (g, b) = Org();
        // The employee BU delegate above the excluded contractor reads the report via the BU grant.
        Assert.True(Gateway(new CountingStore())
            .AuthorizeIndividualRead(g, WithGrant(b.Id("evp"), OrgRole.BuDelegate, b.Id("BU1")), b.Id("ctr_report")).Allowed);
    }

    // === QA ADVERSARIAL (hap-qa) — grant-escalation-bypass attempts ==================================
    // §9.3(a): can an explicit RoleGrant be used to CIRCUMVENT a restriction the grant is not meant to
    // override? Two attempts below; both came back denied (see QA notes in the story file).

    [Fact]
    public async Task Attempt_contractor_manager_bypasses_the_restrictive_exclusion_via_an_explicit_BuDelegate_grant()
    {
        // ATTACK: ctr is a contractor (excluded from individual reads under the Restrictive default,
        // Q-006). Does granting them an explicit BuDelegate scope over their OWN BU let ClassifyReader's
        // grant-first branch launder them past the contractor exclusion? ClassifyReader would classify
        // them BuLead (grant present) and RoleScope.IndividualReadCapability(BuLead)=true, so gate (a)
        // would pass — the exclusion must be enforced downstream by ReaderEligible, independent of any
        // grant, or this is a live bypass.
        var (g, b) = Org();
        var store = new CountingStore();
        var gateway = Gateway(store);
        var ctrAsBuDelegate = new CallerContext(b.Id("ctr"), new[] { new CallerGrant(OrgRole.BuDelegate, b.Id("BU1")) });

        var decision = gateway.AuthorizeIndividualRead(g, ctrAsBuDelegate, b.Id("ctr_report"));
        Assert.False(decision.Allowed,
            "ATTACK: an explicit BuDelegate grant must NOT let a contractor manager bypass the Restrictive exclusion (Q-006).");
        await Assert.ThrowsAsync<AssessmentAccessDeniedException>(() =>
            gateway.ReadIndividualScoresAsync(g, ctrAsBuDelegate, b.Id("ctr_report"), Guid.NewGuid()));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Attempt_manager_with_an_above_BU_grant_reads_their_own_direct_report_denied()
    {
        // ATTACK (defence-in-depth check, not the panel's over-grant): mgr is ind's genuine direct
        // manager AND separately holds a GroupViewer grant. ClassifyReader checks explicit grants FIRST,
        // so the GroupViewer grant reclassifies mgr as GroupLeader (no individual-read capability) even
        // though they are a real, structural direct manager — confirming the gateway fails CLOSED (never
        // open) when a caller's grants and org position disagree, rather than picking whichever is more
        // permissive.
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());
        var mgrWithGroupViewer = new CallerContext(b.Id("mgr"), new[] { new CallerGrant(OrgRole.GroupViewer) });

        Assert.False(gateway.AuthorizeIndividualRead(g, mgrWithGroupViewer, b.Id("ind")).Allowed,
            "ATTACK: a GroupViewer grant must not be overridden by a genuine direct-manager relationship — confirms fail-closed, not fail-open.");
    }

    // --- reader classification is structural (no depth labels) --------------------------------------

    [Fact]
    public void Classify_reader_uses_grants_then_org_position_never_depth()
    {
        var (g, b) = Org();

        Assert.Equal(SeamRole.HigExecutive, AssessmentReads.ClassifyReader(WithGrant(b.Id("evp"), OrgRole.HigExecutive), g).Role);
        Assert.Equal(SeamRole.GroupLeader, AssessmentReads.ClassifyReader(WithGrant(b.Id("evp"), OrgRole.GroupViewer), g).Role);
        Assert.Equal(SeamRole.BuLead, AssessmentReads.ClassifyReader(WithGrant(b.Id("evp"), OrgRole.BuDelegate, b.Id("BU1")), g).Role);
        Assert.Equal(SeamRole.Manager, AssessmentReads.ClassifyReader(Ungranted(b.Id("mgr")), g).Role);   // has a report
        Assert.Equal(SeamRole.Individual, AssessmentReads.ClassifyReader(Ungranted(b.Id("ind")), g).Role); // no reports
    }

    // === AuthorizeModeration (HAP-9) — moderation is the DIRECT manager's grant only ================
    // Stricter than a read: a BU delegate can READ BU-wide but may NOT moderate; the subject may not
    // moderate themselves; a contractor direct manager is excluded (Q-006 / DR-0006).

    [Fact]
    public void Moderation_is_allowed_for_the_direct_manager_only()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());

        // mgr is ind's direct manager → allowed. evp is ind's transitive ancestor (not direct) → denied.
        Assert.True(gateway.AuthorizeModeration(g, Ungranted(b.Id("mgr")), b.Id("ind")).Allowed);
        Assert.False(gateway.AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("ind")).Allowed);
        // evp IS mgr's direct manager → allowed to moderate mgr.
        Assert.True(gateway.AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("mgr")).Allowed);
    }

    [Fact]
    public void Moderation_of_a_direct_report_across_a_BU_boundary_is_allowed()
    {
        var (g, b) = Org();
        // evp directly manages xbu (homed in BU2) — the chain rule governs regardless of BU.
        Assert.True(Gateway(new CountingStore()).AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("xbu")).Allowed);
    }

    [Fact]
    public void Nobody_can_moderate_their_own_assessment()
    {
        var (g, b) = Org();
        Assert.False(Gateway(new CountingStore()).AuthorizeModeration(g, Ungranted(b.Id("ind")), b.Id("ind")).Allowed);
    }

    [Fact]
    public void A_contractor_direct_manager_may_not_moderate_their_report()
    {
        var (g, b) = Org();
        // ctr is ctr_report's direct manager but a contractor → excluded from individual access, so also
        // from moderation (the review escalates to the first employee ancestor, ChainResolver).
        Assert.False(Gateway(new CountingStore()).AuthorizeModeration(g, Ungranted(b.Id("ctr")), b.Id("ctr_report")).Allowed);
    }

    [Fact]
    public void A_BU_delegate_who_is_not_the_direct_manager_may_read_but_not_moderate()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());
        var evpDelegate = WithGrant(b.Id("evp"), OrgRole.BuDelegate, b.Id("BU1"));

        // The BU delegate CAN read ind BU-wide (established above)…
        Assert.True(gateway.AuthorizeIndividualRead(g, evpDelegate, b.Id("ind")).Allowed);
        // …but may NOT moderate ind — moderation follows line management, not the BU-scope grant.
        Assert.False(gateway.AuthorizeModeration(g, evpDelegate, b.Id("ind")).Allowed);
    }

    [Fact]
    public void A_person_with_no_reports_cannot_moderate_anyone()
    {
        var (g, b) = Org();
        Assert.False(Gateway(new CountingStore()).AuthorizeModeration(g, Ungranted(b.Id("stranger")), b.Id("ind")).Allowed);
    }

    // --- DR-0006: read + moderation escalate past a contractor direct manager -----------------------

    [Fact]
    public void Read_and_moderation_escalate_past_a_contractor_direct_manager_to_the_employee_ancestor()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());

        // ctr_report's direct manager is the contractor ctr; its reviewer of record is the employee evp.
        // The contractor gets nothing; the escalated employee ancestor may READ and MODERATE.
        Assert.False(gateway.AuthorizeModeration(g, Ungranted(b.Id("ctr")), b.Id("ctr_report")).Allowed);
        Assert.True(gateway.AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("ctr_report")).Allowed);
        Assert.True(gateway.AuthorizeIndividualRead(g, Ungranted(b.Id("evp")), b.Id("ctr_report")).Allowed);

        // The escalation does NOT open a skip-level read past a VALID employee manager: evp still cannot
        // read/moderate ind (whose reviewer of record is the employee mgr, not evp).
        Assert.False(gateway.AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("ind")).Allowed);
        Assert.False(gateway.AuthorizeIndividualRead(g, Ungranted(b.Id("evp")), b.Id("ind")).Allowed);
    }

    // --- L3: moderation ⊆ read — a capability-less reviewer of record is denied (score oracle) ------

    [Fact]
    public void Moderation_is_denied_for_a_reviewer_of_record_who_has_no_individual_read_capability()
    {
        var (g, b) = Org();
        var gateway = Gateway(new CountingStore());

        // evp is mgr's DIRECT manager (mgr's reviewer of record). As an UNGRANTED manager, evp legitimately
        // moderates mgr…
        Assert.True(gateway.AuthorizeModeration(g, Ungranted(b.Id("evp")), b.Id("mgr")).Allowed);

        // …but the SAME person holding a HIG Executive grant has no individual-read capability (FR-025
        // clause 2), so — even as the reviewer of record — they are denied BOTH the read AND moderation.
        // Without the moderation⊆read gate this returned Allowed and the FR-009 error leaked mgr's score.
        var exec = WithGrant(b.Id("evp"), OrgRole.HigExecutive);
        Assert.False(gateway.AuthorizeIndividualRead(g, exec, b.Id("mgr")).Allowed);
        Assert.False(gateway.AuthorizeModeration(g, exec, b.Id("mgr")).Allowed);

        // Same for a Platform Admin grant and a Group Viewer grant (both aggregates-only).
        Assert.False(gateway.AuthorizeModeration(g, WithGrant(b.Id("evp"), OrgRole.PlatformAdmin), b.Id("mgr")).Allowed);
        Assert.False(gateway.AuthorizeModeration(g, WithGrant(b.Id("evp"), OrgRole.GroupViewer), b.Id("mgr")).Allowed);
    }
}
