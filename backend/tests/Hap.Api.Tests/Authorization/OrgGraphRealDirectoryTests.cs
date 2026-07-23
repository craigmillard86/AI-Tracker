using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// Grounds the seam in the REAL deterministic synthetic directory (research D1: exercise the seam
/// against genuine generator output, not only hand-built graphs). Loads the structural
/// <see cref="OrgGraph"/> through <see cref="OrgGraphLoader"/> and asserts BOTH the chain primitive and
/// the gateway's role-gated decision on the engineered edge cases — including the panel's concrete
/// over-grant path (an above-BU chain ancestor), now closed. Category=PrivacyReporting.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class OrgGraphRealDirectoryTests
{
    private readonly HapApiFactory _factory;

    public OrgGraphRealDirectoryTests(HapApiFactory factory) => _factory = factory;

    // These gateway tests exercise only the AUTHORISATION decision, never a store fetch — the store must
    // never be reached. Every method throws so a reach here is a loud test failure, not a silent pass.
    private sealed class NoStore : IAssessmentStore
    {
        public Task<IReadOnlyList<AssessmentScore>> GetIndividualScoresAsync(
            Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssessmentScore>>(Array.Empty<AssessmentScore>());

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

    private static AssessmentReads Gateway() =>
        new(new ChainResolver(SeamOptions.Default), SeamOptions.Default, new NoStore());

    private async Task SyncCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
    }

    private async Task<(OrgGraph graph, Func<string, Guid> idOf, Func<string, Guid> buIdOf)> LoadAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var graph = await scope.ServiceProvider.GetRequiredService<OrgGraphLoader>().LoadAsync();

        var byRef = await db.People.ToDictionaryAsync(p => p.ExternalRef, p => p.Id);
        var buByCode = await db.BusinessUnits.ToDictionaryAsync(bu => bu.Code, bu => bu.Id);
        return (graph, r => byRef[r], code => buByCode[code]);
    }

    // --- structural load -------------------------------------------------------------------------

    [Fact]
    public async Task Loads_the_whole_structural_population_and_the_containment_maps()
    {
        await SyncCanonicalAsync();
        var (graph, _, _) = await LoadAsync();

        Assert.True(graph.People.Count >= 10_000, $"expected the full synthetic population, got {graph.People.Count}");
        Assert.Equal(23, graph.AllBusinessUnitIds.Count);
    }

    // --- THE PANEL'S OVER-GRANT PATH, NOW CLOSED -------------------------------------------------

    [Fact]
    public async Task Above_BU_chain_ancestor_is_denied_by_the_gateway_even_though_the_chain_grants_it()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var chain = new ChainResolver(SeamOptions.Default);
        var gateway = Gateway();

        var groupLeader = idOf(Distributions.GroupLeaderRef(1)); // HAP-GRP-01
        var seedInd = idOf(Distributions.SeedIndividualRef);

        // The chain PRIMITIVE still reports the group leader as an ancestor (that was the red-team's path)…
        Assert.True(chain.GrantsIndividualRead(graph, groupLeader, seedInd));
        // …but the GATEWAY denies it: an ungranted transitive ancestor is not attributable to a within-BU
        // role without Q-014, so it fails closed (FR-025 clause 2 / Q-015).
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(groupLeader), seedInd).Allowed);
    }

    // === PINNING TEST — the one-hop above-BU direct read, RATIFIED per DR-0005 =======================
    // What is CLOSED: the gross transitive/subtree over-grant (an above-BU leader reading individuals
    // several hops down — see the test above). What DR-0005 RATIFIES as ALLOW (owner ruling 2026-07-21,
    // docs/decisions/DR-0005-above-bu-direct-report-read.md): an ungranted hierarchy above-BU leader
    // (Portfolio/Group Leader — the generator seeds them NO explicit OrgRole grant, only Platform Admin and
    // HIG Executive get grants) falls through ClassifyReader to Manager (they "have direct reports"), so they
    // CAN read the individual scores of their IMMEDIATE DIRECT reports — a Group Leader reads their
    // direct-report BU Lead; a Portfolio Leader reads their direct-report Group Leader. This is a direct
    // line-manager read for moderation, and the owner ratified it as intended behaviour: transitive/subtree
    // reads stay denied, the broad above-BU view stays aggregates-only, and the one-hop direct read is
    // allowed regardless of tier. This test now DOCUMENTS the ratified behaviour (Q-015 resolved, block
    // lifted); the assertions are unchanged from when it pinned the residual — the framing, not the
    // behaviour, is what DR-0005 settled. (Q-014's real-org caveat is a deferred, separate concern: on real
    // org shapes an above-BU BU-wide read still needs an explicit grant — fine for the synthetic build.)
    [Fact]
    public async Task PINNED_ungranted_above_BU_hierarchy_leader_CAN_read_immediate_direct_report_ratified_DR0005()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var portfolioLeader = idOf(Distributions.PortfolioLeaderRef(1)); // HAP-PF-01
        var groupLeader = idOf(Distributions.GroupLeaderRef(1));         // HAP-GRP-01 (direct report of PF-01)
        var buLead = idOf(Distributions.BuLeadRef(1));                   // HAP-BUL-01 (direct report of GRP-01)

        // RATIFIED ALLOW per DR-0005 — a direct line-manager read for moderation, allowed regardless of tier:
        Assert.True(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(groupLeader), buLead).Allowed,
            "Group Leader reading their direct-report BU Lead — one-hop direct read, ratified ALLOW (DR-0005).");
        Assert.True(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(portfolioLeader), groupLeader).Allowed,
            "Portfolio Leader reading their direct-report Group Leader — one-hop direct read, ratified ALLOW (DR-0005).");

        // CONTRAST — the gross over-grant that IS closed now: the SAME Portfolio Leader cannot reach two hops
        // down to the BU Lead (transitive), even though the chain makes them an ancestor.
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(portfolioLeader), buLead).Allowed,
            "Transitive (two-hop) above-BU read stays denied — that gross over-grant is closed.");
    }

    [Fact]
    public async Task Hig_executive_grant_reads_no_individual_scores()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var exec = idOf(Distributions.ExecRef);
        var seedInd = idOf(Distributions.SeedIndividualRef);

        var context = new CallerContext(exec, new[] { new CallerGrant(OrgRole.HigExecutive) });
        Assert.False(Gateway().AuthorizeIndividualRead(graph, context, seedInd).Allowed);
    }

    // === QA ADVERSARIAL (hap-qa, fresh instance) — WIDEN-THE-RESIDUAL ATTEMPTS ======================
    // §9.3(a): confirm the documented one-hop-above-BU-hierarchy-leader residual (PINNED test above) is
    // EXACTLY one-hop-direct and does not extend to (i) a 2+ hop transitive read reached via a DIFFERENT
    // branch, (ii) a same-tier sibling who is NOT the caller's direct report, or (iii) an ordinary
    // individual several hops down. Every assertion below is an ATTACK — a True here would be a NEW,
    // undocumented defect beyond the recorded Q-015 residual. All came back DENIED (see QA notes in the
    // story file for the full attempt log).

    [Fact]
    public async Task Attempt_group_leader_reads_a_bu_lead_outside_their_own_group_denied()
    {
        // GRP-01 manages BU01-04's leads (group 1: BusPerGroup[0]=4). BUL-05 belongs to group 2
        // (GRP-02's direct report), NOT GRP-01's. If this widened to ALLOW, the residual would no longer
        // be "direct report only" — it would be "any same-tier hierarchy node", a materially bigger leak.
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var groupLeader1 = idOf(Distributions.GroupLeaderRef(1));
        var buLead5 = idOf(Distributions.BuLeadRef(5)); // BU05 — group 2, not group 1

        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(groupLeader1), buLead5).Allowed,
            "ATTACK: GRP-01 reading a BU Lead who is NOT their direct report (different group) must stay denied.");
    }

    [Fact]
    public async Task Attempt_portfolio_leader_reads_a_group_leader_outside_their_own_portfolio_denied()
    {
        // PF-01 owns portfolio 1 = groups 1-2 (GroupCount/PortfolioCount=2 groups/portfolio). GRP-03
        // belongs to portfolio 2 (PF-02's direct report), NOT PF-01's.
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var portfolioLeader1 = idOf(Distributions.PortfolioLeaderRef(1));
        var groupLeader3 = idOf(Distributions.GroupLeaderRef(3)); // group 3 — portfolio 2, not portfolio 1

        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(portfolioLeader1), groupLeader3).Allowed,
            "ATTACK: PF-01 reading a Group Leader who is NOT their direct report (different portfolio) must stay denied.");
    }

    [Fact]
    public async Task Attempt_portfolio_leader_reads_an_ordinary_individual_four_hops_down_denied()
    {
        // PF-01 -> GRP-01 -> BUL-01 -> SeedMgr -> SeedInd is FOUR hops. If the pinned one-hop residual
        // ever widened to "any hierarchy descendant", this is the deepest, highest-value target.
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var portfolioLeader1 = idOf(Distributions.PortfolioLeaderRef(1));
        var seedInd = idOf(Distributions.SeedIndividualRef);

        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(portfolioLeader1), seedInd).Allowed,
            "ATTACK: PF-01 reading an ordinary individual 4 hops down must stay denied — the residual is one-hop only.");
    }

    // === QA ADVERSARIAL — §9.3(a) per-seeded-role sweep (the mandatory "read a score outside the
    // management chain, as each seeded role" attempt). Individual/Manager/HigExecutive/PlatformAdmin
    // covered here against the real directory; BuLead/GroupLeader/PortfolioLeader covered above and in
    // the existing dev tests (fail-closed transitive + the documented one-hop residual). ================

    [Fact]
    public async Task Attempt_individual_reads_their_own_manager_or_a_stranger_denied()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var seedInd = idOf(Distributions.SeedIndividualRef);
        var seedMgr = idOf(Distributions.SeedManagerRef);
        var buLead1 = idOf(Distributions.BuLeadRef(1));

        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(seedInd), seedMgr).Allowed,
            "ATTACK: an Individual reading their own manager (up the chain) must be denied.");
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(seedInd), buLead1).Allowed,
            "ATTACK: an Individual reading a stranger several hops up must be denied.");
    }

    [Fact]
    public async Task Attempt_manager_reads_a_skip_level_or_non_report_denied()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var seedMgr = idOf(Distributions.SeedManagerRef);
        var buLead1 = idOf(Distributions.BuLeadRef(1));           // skip-level UP from mgr
        var crossReport = idOf(Distributions.CrossBuReportRef);   // a DIFFERENT manager's (buLead1's) report

        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(seedMgr), buLead1).Allowed,
            "ATTACK: a Manager reading up the chain (their own manager) must be denied.");
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(seedMgr), crossReport).Allowed,
            "ATTACK: a Manager reading a peer's report (not their own direct report) must be denied.");
    }

    [Fact]
    public async Task Attempt_hig_executive_reads_even_their_own_direct_report_denied()
    {
        // Exec directly manages all three Portfolio Leaders (PF-01/02/03 report to HAP-EXEC). This is
        // the strongest possible attempt for an Exec — a genuine direct-report read — and it must still
        // be denied, because RoleScope.IndividualReadCapability(HigExecutive) is None by construction
        // regardless of chain position (FR-025 clause 2; domain advisory A2, intentional).
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var exec = idOf(Distributions.ExecRef);
        var portfolioLeader1 = idOf(Distributions.PortfolioLeaderRef(1)); // Exec's direct report

        var context = new CallerContext(exec, new[] { new CallerGrant(OrgRole.HigExecutive) });
        Assert.False(Gateway().AuthorizeIndividualRead(graph, context, portfolioLeader1).Allowed,
            "ATTACK: HIG Executive reading their OWN direct report must still be denied — no individual-read capability at all.");
    }

    [Fact]
    public async Task Attempt_platform_admin_reads_any_individual_score_including_their_own_manager_denied()
    {
        // Platform Admin (HAP-ADMIN) is homed under BUL-01 (their own direct manager). Admin holds no
        // individual-read capability by construction (framework/cycle/org administration only).
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var admin = idOf(Distributions.AdminRef);
        var buLead1 = idOf(Distributions.BuLeadRef(1)); // admin's own manager
        var seedInd = idOf(Distributions.SeedIndividualRef); // an unrelated stranger

        var context = new CallerContext(admin, new[] { new CallerGrant(OrgRole.PlatformAdmin) });
        var gateway = Gateway();
        Assert.False(gateway.AuthorizeIndividualRead(graph, context, buLead1).Allowed,
            "ATTACK: Platform Admin reading their own manager must be denied.");
        Assert.False(gateway.AuthorizeIndividualRead(graph, context, seedInd).Allowed,
            "ATTACK: Platform Admin reading an unrelated individual must be denied.");
    }

    // --- the reads that must keep working --------------------------------------------------------

    [Fact]
    public async Task Direct_manager_reads_their_report_including_the_cross_BU_case()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, _) = await LoadAsync();
        var gateway = Gateway();

        var seedMgr = idOf(Distributions.SeedManagerRef);
        var seedInd = idOf(Distributions.SeedIndividualRef);
        var buLead1 = idOf(Distributions.BuLeadRef(1));
        var crossReport = idOf(Distributions.CrossBuReportRef); // homed in BU02, managed from BU01

        Assert.True(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(seedMgr), seedInd).Allowed);
        Assert.True(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(buLead1), crossReport).Allowed);
    }

    [Fact]
    public async Task Bu_delegate_reads_bu_wide_but_an_ungranted_bu_lead_fails_closed()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, buIdOf) = await LoadAsync();
        var gateway = Gateway();

        var buLead1 = idOf(Distributions.BuLeadRef(1));
        var seedInd = idOf(Distributions.SeedIndividualRef); // BU01, transitive report of the BU Lead

        // Ungranted BU Lead → cannot attribute a transitive read without Q-014 → fail closed.
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(buLead1), seedInd).Allowed);

        // With an explicit BU-scope grant for BU01, the BU-wide read is authorised.
        var delegated = new CallerContext(buLead1, new[] { new CallerGrant(OrgRole.BuDelegate, buIdOf("BU01")) });
        Assert.True(gateway.AuthorizeIndividualRead(graph, delegated, seedInd).Allowed);
    }

    // --- contractor manager (Q-006) --------------------------------------------------------------

    [Fact]
    public async Task Contractor_manager_is_denied_but_an_employee_bu_delegate_above_reads_the_report()
    {
        await SyncCanonicalAsync();
        var (graph, idOf, buIdOf) = await LoadAsync();
        var gateway = Gateway();

        var contractorMgr = idOf(Distributions.ContractorManagerRef);
        var report = idOf(Distributions.ContractorManagerRef + "-R1");
        var buLead = idOf(Distributions.BuLeadRef(1));

        // The contractor is the report's direct manager, yet gets no individual-score access (Q-006).
        Assert.False(gateway.AuthorizeIndividualRead(graph, CallerContext.Ungranted(contractorMgr), report).Allowed);

        // The employee BU delegate above the contractor reads the report through the BU grant, and the
        // chain still resolves the reviewer of record past the excluded contractor (FR-070).
        var delegated = new CallerContext(buLead, new[] { new CallerGrant(OrgRole.BuDelegate, buIdOf(Distributions.BuCode(1))) });
        Assert.True(gateway.AuthorizeIndividualRead(graph, delegated, report).Allowed);
        Assert.Equal(buLead, new ChainResolver(SeamOptions.Default).ReviewerOfRecord(graph, report));
    }
}
