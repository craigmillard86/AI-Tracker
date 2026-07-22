using System.Net;
using System.Net.Http.Json;
using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Hap.Synth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-9 (fresh-instance QA pass, CLAUDE.md §9), added during QA
/// rather than Dev — attributed as QA work. Dev's own suite (<see cref="TeamModerationEndpointsTests"/>)
/// and the gateway unit tests (<see cref="Authorization.AssessmentReadsGatewayTests"/>) already prove
/// moderation⊆read against a hand-built BU01 fixture and one capability-less-manager case (ADMIN over
/// EMPADM). This file targets exactly what the round-3 L3 panel handed to hap-qa (story notes, "Panel
/// round 3"): the full seven-seeded-role cross-chain sweep on the CANONICAL synthetic directory, the
/// Exec-over-Portfolio-Leader capability-less reviewer-of-record (the concrete case the red-team finding
/// named), a genuine 3-level skip-level chain, and an explicit-grant holder (BuDelegate/GroupViewer)
/// exercised at the live HTTP endpoint — not just the gateway — to prove
/// <see cref="ManagerModerationService"/>'s <c>CallerContextAsync</c> reads the caller's REAL grants
/// rather than a stripped <see cref="CallerContext.Ungranted"/> context. Also independently recomputes
/// the moderated score of record from raw rows (§9.3(c) methodology) even though HAP-9 itself produces
/// no rollup/Harris aggregate.
/// </summary>
[Collection("hap-db")]
public sealed class TeamModerationQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public TeamModerationQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    // --- setup helpers (canonical 23-BU synthetic directory — the real seven-role fixture) ---------

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            // Onboard every BU so no seeded/edge person is excluded from the cycle for an unrelated
            // reason (NotOnboarded) — this file is about cross-person REACH, not participation.
            var bus = await db.BusinessUnits.ToListAsync();
            foreach (var bu in bus)
            {
                bu.SetOnboarded(true);
            }
            await db.SaveChangesAsync();
        }
        // The local dev provider only allows sign-in as a REGISTERED seed user (LocalDevProvider.
        // SignInAsync throws UnknownSeedUserException otherwise) — the canonical seven-role list alone
        // is not enough for tests that also need to sign in as a named EDGE-CASE fixture (a peer manager,
        // a grandmanager, a team-of-four report) that exists in the directory but isn't one of the seven
        // labelled roles. Extend the list with a distinct "QA-Fixture" role label so it never collides
        // with RefForRole's per-role Single() lookup over the canonical seven.
        var seedUsers = _factory.CanonicalSeedUsers
            .Concat(new[]
            {
                Snap.SeedUser(Distributions.Team3ManagerRef, role: "QA-Fixture", buCode: Distributions.BuCode(1)),
                Snap.SeedUser(Distributions.Team4ManagerRef + "-R1", role: "QA-Fixture", buCode: Distributions.OrgOfSevenBuCode),
                Snap.SeedUser(Distributions.BuLeadRef(4), role: "QA-Fixture", buCode: Distributions.OrgOfSevenBuCode),
            })
            .ToList();
        _factory.SeedUsers.Inner = new StubSeedUserSource(seedUsers);

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, Distributions.AdminRef);
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid frameworkVersionId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, name, true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return cycle.Id;
    }

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    /// <summary>Signs in as <paramref name="externalRef"/>, fills every dimension with
    /// <paramref name="score"/> (+ optional evidence fingerprint), and submits.</summary>
    private async Task SubmitSelfAsync(string externalRef, int score, string? evidence = null)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, evidence)).ToList()));
        var submit = await client.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<string> DisplayNameAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).DisplayName;
    }

    private async Task<Guid> AssessmentIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        return await db.Set<Assessment>()
            .Where(a => a.PersonId == personId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Id)
            .FirstAsync();
    }

    private async Task<int> IndividualViewCountAsync(Guid subjectPersonId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.AuditLogs.CountAsync(a => a.Action == AuditAction.IndividualView && a.SubjectPersonId == subjectPersonId);
    }

    private async Task<int> ScoreChangeCountAsync(Guid subjectPersonId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.AuditLogs.CountAsync(a => a.Action == AuditAction.ScoreChange && a.SubjectPersonId == subjectPersonId);
    }

    private async Task<AssessmentState> AssessmentStateAsync(Guid assessmentId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId)).State;
    }

    private static Task<HttpResponseMessage> ModerateAsync(HttpClient client, Guid assessmentId, params ModerateDecision[] decisions) =>
        client.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions));

    private string RefForRole(string role) => _factory.CanonicalSeedUsers.Single(u => u.Role == role).ExternalRef;

    /// <summary>Grant an explicit <see cref="OrgRole"/> directly via the RoleGrant table (test
    /// scaffolding — no admin role-grant endpoint exists yet, Q-013), so the HTTP-level tests below can
    /// prove <c>CallerContextAsync</c>'s per-request DB read actually sees it.</summary>
    private async Task GrantAsync(string externalRef, OrgRole role, string? businessUnitCode = null)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        Guid? buId = businessUnitCode is null
            ? null
            : (await db.BusinessUnits.SingleAsync(b => b.Code == businessUnitCode)).Id;
        db.RoleGrants.Add(RoleGrant.Create(personId, role, buId, grantedBy: "qa-test"));
        await db.SaveChangesAsync();
    }

    // =================================================================================================
    // §9.3(a) — mandatory: as EACH seeded role, attempt to read a self-score OUTSIDE the caller's reach
    // via GET /api/team/reviews (queue), GET /api/team/members/{id}/assessment, and the moderation-422
    // path (PUT with a bad divergence, probing for a score-oracle leak in the error body). All seven
    // canonical role-holders are structurally unrelated to the victim (a different-BU stranger), so
    // every attempt must be denied — some by the role-capability gate (Exec/Admin/GroupViewer-shaped
    // roles), some by the reviewer-of-record/reach mismatch (Individual/Manager/BuLead/PortfolioLeader
    // classified as plain Managers via HasDirectReports, DR-0005) — but denied either way.
    // =================================================================================================

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData("Individual")]
    [InlineData("Manager")]
    [InlineData("BU Lead")]
    [InlineData("Group Leader")]
    [InlineData("Portfolio Leader")]
    [InlineData("HIG Executive")]
    [InlineData("Platform Admin")]
    public async Task No_seeded_role_can_read_or_moderate_a_structurally_unrelated_persons_assessment(string attackerRole)
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // The victim: BU04's engineered team-4 report (Team4ManagerRef-R1) — homed in a different BU
        // than any of the seven canonical role fixtures below, so none of them is the victim's direct
        // manager or reviewer of record. (An above-BU role IS a transitive ancestor via the single-tree
        // org — that must still be denied, DR-0005/Q-014 fail-closed on transitive reads.)
        const string victimRef = Distributions.Team4ManagerRef + "-R1";
        const string fingerprint = "QA-HAP9-SWEEP-FINGERPRINT";
        await SubmitSelfAsync(victimRef, 3, fingerprint);
        var victimId = await PersonIdAsync(victimRef);
        var assessmentId = await AssessmentIdAsync(victimRef);
        var victimName = await DisplayNameAsync(victimRef);

        var attackerRef = RefForRole(attackerRole);
        var attacker = await ClientAsync(attackerRef);

        // (1) Queue must never list the victim.
        var queue = (await (await attacker.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.DoesNotContain(queue.Reviews, r => r.DisplayName == victimName);

        // (2) The [A] member read: 404 (existence-leak), zero IndividualView rows — before AND after.
        Assert.Equal(0, await IndividualViewCountAsync(victimId));
        var getResponse = await attacker.GetAsync($"/api/team/members/{victimId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(victimId));

        // (3) The moderation-422 path: a deliberately wrong divergent score, probing whether the FR-009
        // forced-comment error body ever surfaces the victim's real self-score (3) as an oracle. Denied
        // callers never reach the validation layer (moderation ⊆ read) — 404, not 422 — and the body
        // must not contain the fingerprint or self-score under any circumstance.
        var putResponse = await ModerateAsync(attacker, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fingerprint, putBody);

        // Nothing about the victim's record moved.
        Assert.Equal(0, await IndividualViewCountAsync(victimId));
        Assert.Equal(0, await ScoreChangeCountAsync(victimId));
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
    }

    // =================================================================================================
    // REQUIRED CASE 1 — HIG Executive as the LITERAL direct reviewer-of-record of a Portfolio Leader.
    // This is the exact shape of the real leak the L3 red-team found and batch-3 fixed (moderation ⊆
    // read, root-caused by the endpoints discarding the caller's real grants). Proves it stays closed
    // at the HTTP endpoint level with the canonical generator's REAL org wiring (Exec directly manages
    // every Portfolio Leader — DirectoryGenerator.cs), not a hand-built proxy fixture.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task HIG_Executive_who_is_the_direct_reviewer_of_record_of_a_Portfolio_Leader_is_denied_all_three_surfaces()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var portfolioLeaderRef = Distributions.PortfolioLeaderRef(1);
        const string fingerprint = "QA-HAP9-EXEC-OVER-PORTFOLIO-LEADER";
        await SubmitSelfAsync(portfolioLeaderRef, 3, fingerprint);
        var plId = await PersonIdAsync(portfolioLeaderRef);
        var assessmentId = await AssessmentIdAsync(portfolioLeaderRef);
        var plName = await DisplayNameAsync(portfolioLeaderRef);

        // Confirm the fixture premise: the Exec IS structurally the Portfolio Leader's direct manager
        // (their reviewer of record), so a violation here would be the real leak, not a strawman.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var pl = await db.People.SingleAsync(p => p.Id == plId);
            var execPerson = await db.People.SingleAsync(p => p.ExternalRef == Distributions.ExecRef);
            Assert.Equal(execPerson.Id, pl.ManagerPersonId); // premise: Exec is the LITERAL direct manager
        }

        var exec = await ClientAsync(Distributions.ExecRef);

        // Queue: empty / not-a-manager — must not leak the Portfolio Leader's name or state.
        var queue = (await (await exec.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.False(queue.IsManager);
        Assert.DoesNotContain(queue.Reviews, r => r.DisplayName == plName);

        // GET: 404, no audit row.
        var getResponse = await exec.GetAsync($"/api/team/members/{plId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(plId));

        // PUT (moderation-422 oracle probe): 404, no score in the body, nothing persisted.
        var putResponse = await ModerateAsync(exec, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fingerprint, putBody);
        Assert.DoesNotContain("3", putBody); // the self-score itself, in case it leaked unlabelled
        Assert.Equal(0, await ScoreChangeCountAsync(plId));
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
    }

    // =================================================================================================
    // REQUIRED CASE 2 — a genuine 3-level chain: grandmanager denied, no skip-level, even though the
    // grandmanager (a plain hierarchy BU Lead, ClassifyReader falls them through HasDirectReports) DOES
    // hold individual-read capability in the abstract — this isolates the reach/reviewer-of-record rule
    // from the role-capability gate REQUIRED CASE 1 exercised.
    // BuLeadRef(OrgOfSeven) --direct--> Team4ManagerRef --direct--> Team4ManagerRef-R1 (grandchild).
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_grandmanager_two_hops_above_an_employee_is_denied_no_skip_level()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        const string grandchildRef = Distributions.Team4ManagerRef + "-R1";
        const string fingerprint = "QA-HAP9-SKIP-LEVEL-FINGERPRINT";
        await SubmitSelfAsync(grandchildRef, 2, fingerprint);
        var grandchildId = await PersonIdAsync(grandchildRef);
        var assessmentId = await AssessmentIdAsync(grandchildRef);

        var buLeadOfSeven = Distributions.BuLeadRef(4); // Distributions.OrgOfSevenBuCode == "BU04"

        // Confirm the premise: a genuine 3-level chain, the middle manager active and non-contractor.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var grandchild = await db.People.SingleAsync(p => p.Id == grandchildId);
            var directManager = await db.People.SingleAsync(p => p.Id == grandchild.ManagerPersonId);
            var grandmanagerPerson = await db.People.SingleAsync(p => p.ExternalRef == buLeadOfSeven);
            Assert.Equal(Distributions.Team4ManagerRef, directManager.ExternalRef);
            Assert.Equal(EmployeeType.Employee, directManager.EmployeeType);
            Assert.True(directManager.IsActive);
            Assert.Equal(grandmanagerPerson.Id, directManager.ManagerPersonId); // BuLead is the DIRECT manager's manager
            Assert.NotEqual(grandmanagerPerson.Id, grandchild.ManagerPersonId); // …but NOT the grandchild's direct manager
        }

        var grandmanager = await ClientAsync(buLeadOfSeven);

        // The grandmanager DOES have a real team (Team4ManagerRef among their direct reports) — so this
        // isn't a capability-less caller; the queue legitimately shows IsManager=true, just never the
        // grandchild by name.
        var queue = (await (await grandmanager.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.True(queue.IsManager);
        Assert.DoesNotContain(queue.Reviews, r => r.PersonId == grandchildId);

        var getResponse = await grandmanager.GetAsync($"/api/team/members/{grandchildId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(grandchildId));

        var putResponse = await ModerateAsync(grandmanager, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
        var putBody = await putResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fingerprint, putBody);
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
    }

    // =================================================================================================
    // REQUIRED CASE 3 — an explicit-grant holder exercised at the LIVE HTTP endpoint (not the gateway
    // unit tests), to confirm ManagerModerationService.CallerContextAsync reads the caller's REAL grants
    // from the database on every request, not a stripped Ungranted context. Both directions: a BuDelegate
    // grant ENABLING a read that was previously denied, and a GroupViewer grant STRIPPING a read that was
    // previously allowed (a genuine direct-manager relationship) — proving the grant is actually
    // consulted, not silently ignored in either direction.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task An_explicit_BuDelegate_grant_enables_a_previously_denied_read_at_the_live_endpoint()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // Team3ManagerRef is a peer manager in BU01 with NO structural relationship to
        // SeedIndividualRef (who reports to SeedManagerRef, a different team lead in the same BU).
        const string fingerprint = "QA-HAP9-BUDELEGATE-FINGERPRINT";
        await SubmitSelfAsync(Distributions.SeedIndividualRef, 2, fingerprint);
        var victimId = await PersonIdAsync(Distributions.SeedIndividualRef);

        var peerManager = await ClientAsync(Distributions.Team3ManagerRef);

        // BEFORE the grant: denied — a peer manager, no relation.
        var before = await peerManager.GetAsync($"/api/team/members/{victimId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(victimId));

        // Grant Team3ManagerRef an explicit BuDelegate over BU01 (the victim's home BU) directly via the
        // RoleGrant table (test scaffolding — no admin grant endpoint exists yet, Q-013).
        await GrantAsync(Distributions.Team3ManagerRef, OrgRole.BuDelegate, Distributions.BuCode(1));

        // AFTER the grant, on the SAME live endpoint, with a FRESH request: allowed, one audit row, and
        // the actual data (not a stripped/Ungranted denial). This is the load-bearing proof — if
        // CallerContextAsync silently discarded the grant (as the pre-batch-3 endpoints did for the
        // OTHER direction), this would still 404.
        var after = await peerManager.GetAsync($"/api/team/members/{victimId}/assessment");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var view = (await after.Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        Assert.All(view.Dimensions, d => Assert.Equal(fingerprint, d.SelfEvidence));
        Assert.Equal(1, await IndividualViewCountAsync(victimId));

        // The grant does NOT extend to moderation (BU delegate reads BU-wide but moderates only their
        // own reviewee-of-record, AssessmentReads.AuthorizeModeration conjunct 2) — Team3ManagerRef still
        // cannot moderate SeedIndividualRef.
        var assessmentId = await AssessmentIdAsync(Distributions.SeedIndividualRef);
        var moderateAttempt = await ModerateAsync(peerManager, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null));
        Assert.Equal(HttpStatusCode.NotFound, moderateAttempt.StatusCode);
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task An_explicit_GroupViewer_grant_strips_a_genuine_direct_managers_read_at_the_live_endpoint()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        const string fingerprint = "QA-HAP9-GROUPVIEWER-STRIP-FINGERPRINT";
        await SubmitSelfAsync(Distributions.SeedIndividualRef, 1, fingerprint);
        var reportId = await PersonIdAsync(Distributions.SeedIndividualRef);
        var assessmentId = await AssessmentIdAsync(Distributions.SeedIndividualRef);

        var directManager = await ClientAsync(Distributions.SeedManagerRef);

        // BEFORE any grant: SeedManagerRef IS the genuine direct manager — allowed (sanity check that
        // the fixture premise holds and the endpoint isn't already broken some other way).
        var before = await directManager.GetAsync($"/api/team/members/{reportId}/assessment");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(reportId));

        // Grant SeedManagerRef an explicit GroupViewer (aggregates-only, FR-025 clause 2) directly via
        // the RoleGrant table.
        await GrantAsync(Distributions.SeedManagerRef, OrgRole.GroupViewer);

        // AFTER the grant: denied — even though the structural direct-manager relationship is unchanged
        // and was allowed a moment ago. This proves ClassifyReader's grants-first ordering is consulted
        // for real at the live endpoint (fails closed, does not silently keep the more-permissive org
        // position) — the same defence-in-depth property the gateway unit tests pin, now proven through
        // the full HTTP stack including CallerContextAsync's live DB read.
        var after = await directManager.GetAsync($"/api/team/members/{reportId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(reportId)); // unchanged — the denial wrote no new row

        var moderateAttempt = await ModerateAsync(directManager, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null));
        Assert.Equal(HttpStatusCode.NotFound, moderateAttempt.StatusCode);
        var body = await moderateAttempt.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fingerprint, body);
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
    }

    // =================================================================================================
    // §9.3(b) — N/A to HAP-9 by inspection: this story computes no aggregate/rollup — GET /api/team/
    // reviews and GET /api/team/members/{id}/assessment return per-person state/scores only, never a
    // count or mean over ≥1 people. Confirmed by reading TeamEndpoints.cs / ManagerModerationService.cs
    // top to bottom (no `[S]` marker, no Suppression.cs reference, no aggregation query) — recorded here
    // as an executable assertion of the shape claim rather than prose alone: the response types carry no
    // count/mean/aggregate field at all.
    // =================================================================================================

    [Fact]
    public void HAP_9_response_shapes_carry_no_aggregate_or_count_field_N_A_by_construction()
    {
        // Whole-name match (not Contains) — a substring check over words like "PersonId"/"OnLeave"/
        // "CanModerate" would false-positive on the letter "n" in almost every property name.
        var allNames = typeof(TeamReviewsResponse).GetProperties().Select(p => p.Name)
            .Concat(typeof(TeamReviewItemResponse).GetProperties().Select(p => p.Name))
            .Concat(typeof(MemberAssessmentResponse).GetProperties().Select(p => p.Name))
            .Concat(typeof(MemberDimensionResponse).GetProperties().Select(p => p.Name))
            .ToList();
        var forbiddenWholeNames = new[] { "Count", "Mean", "N", "Suppressed", "SuppressionReason", "Aggregate", "FloorLevelDistribution" };
        foreach (var forbidden in forbiddenWholeNames)
        {
            Assert.DoesNotContain(allNames, name => string.Equals(name, forbidden, StringComparison.Ordinal));
        }
    }

    // =================================================================================================
    // §9.3(c) — independent recomputation: the moderated score of record is exactly one atomic write.
    // Recompute self/manager scores per dimension directly from the AssessmentScore table (bypassing the
    // API entirely) and prove they equal what GET /api/me/assessment/result reports byte-for-byte, and
    // that exactly one Moderated assessment row / one ScoreChange audit row exist — no desync between
    // what the individual is told and what is actually on record.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task The_moderated_result_the_individual_sees_matches_an_independent_raw_row_query_exactly()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync(Distributions.SeedIndividualRef, 3);
        var reportId = await PersonIdAsync(Distributions.SeedIndividualRef);
        var assessmentId = await AssessmentIdAsync(Distributions.SeedIndividualRef);

        var manager = await ClientAsync(Distributions.SeedManagerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{reportId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;

        // Alternate adopt(3)/diverge(1, commented) so every Δ≥2 row is legally commented and the
        // raw-row recompute below has real divergence to check.
        var decisions = view.Dimensions
            .Select((d, i) => i % 2 == 0
                ? new ModerateDecision(d.DimensionId, 3, null)
                : new ModerateDecision(d.DimensionId, 1, "QA recompute — moderated to L1"))
            .ToArray();
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(manager, assessmentId, decisions)).StatusCode);

        // Independent raw-row query — a fresh scope, fresh EF query, no reuse of any seam/service code.
        Dictionary<Guid, (int Self, int Manager, string? Comment)> raw;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            raw = await db.Set<AssessmentScore>()
                .Where(s => s.AssessmentId == assessmentId)
                .ToDictionaryAsync(s => s.DimensionId, s => (s.SelfScore, s.ManagerScore!.Value, s.ManagerComment));
        }

        var individual = await ClientAsync(Distributions.SeedIndividualRef);
        var result = (await (await individual.GetAsync("/api/me/assessment/result"))
            .Content.ReadFromJsonAsync<AssessmentResultResponse>())!;

        Assert.Equal(raw.Count, result.Dimensions.Count);
        foreach (var d in result.Dimensions)
        {
            var (self, mgr, comment) = raw[d.DimensionId];
            Assert.Equal(self, d.SelfScore);
            Assert.Equal(mgr, d.ManagerScore);
            Assert.Equal(Math.Abs(self - mgr), d.Divergence);
            Assert.Equal(comment, d.ManagerComment);
        }

        // Exactly one score-of-record write: one Moderated assessment, one ScoreChange row.
        Assert.Equal(AssessmentState.Moderated, await AssessmentStateAsync(assessmentId));
        Assert.Equal(1, await ScoreChangeCountAsync(reportId));
    }

    // =================================================================================================
    // RED-TEAM BRIEF — negative-path additions the dev/panel rounds did not exercise: an unknown
    // dimension in the moderation payload (ModerationDimensionException, untested by Dev's own suite,
    // which only ever names real dimensions or omits decisions) and an unknown personId on the member
    // read (subject not in the org graph at all, not merely out of reach).
    // =================================================================================================

    [Fact]
    public async Task Moderating_a_dimension_the_report_never_scored_is_422_and_persists_nothing()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync(Distributions.SeedIndividualRef, 2);
        var assessmentId = await AssessmentIdAsync(Distributions.SeedIndividualRef);
        var reportId = await PersonIdAsync(Distributions.SeedIndividualRef);

        var manager = await ClientAsync(Distributions.SeedManagerRef);
        var response = await ModerateAsync(manager, assessmentId, new ModerateDecision(Guid.NewGuid(), 2, null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(AssessmentState.Submitted, await AssessmentStateAsync(assessmentId));
        Assert.Equal(0, await ScoreChangeCountAsync(reportId));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Reading_or_moderating_a_person_id_absent_from_the_org_graph_entirely_is_404()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var manager = await ClientAsync(Distributions.SeedManagerRef);
        var randomPersonId = Guid.NewGuid(); // not in the org graph at all, not merely out of reach

        var getResponse = await manager.GetAsync($"/api/team/members/{randomPersonId}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(randomPersonId));

        var putResponse = await ModerateAsync(manager, Guid.NewGuid()); // unknown assessment id too
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
    }
}
