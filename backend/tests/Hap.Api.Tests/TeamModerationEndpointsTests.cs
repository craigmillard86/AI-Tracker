using System.Net;
using System.Net.Http.Json;
using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-9 acceptance criteria for the manager-moderation endpoints (contracts/api.md "Manager scope";
/// FR-008/009/010/011/012/063/069). Exercises the review queue, the [A] member-assessment read (incl.
/// the L3 boundaries — exactly-one audit row, out-of-chain 404-with-no-audit, audit fail-closed), the
/// moderation write (adopt-self / carry-forward defaults, the Δ≥2 comment rule, the Q-017a post-close
/// submission lock, state + authorisation guards, the ScoreChange audit), and the individual result view.
///
/// <para>Fixture: BU01 onboarded; ADMIN (Platform Admin), MGR1 (manager of EMP1/EMP2/LEAVE1), MGR2
/// (manager of EMP3 — a different chain), EMP1/EMP2/EMP3 (Individuals), LEAVE1 (on-leave report of
/// MGR1). The framework seeds seven dimensions with 0–3 descriptors.</para>
/// </summary>
[Collection("hap-db")]
public sealed class TeamModerationEndpointsTests
{
    private readonly HapApiFactory _factory;

    public TeamModerationEndpointsTests(HapApiFactory factory) => _factory = factory;

    // --- setup helpers ---------------------------------------------------------------------------

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("MGR2", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("EMP3", "BU01", managerExternalRef: "MGR2"),
                Snap.Person("LEAVE1", "BU01", managerExternalRef: "MGR1", onLeave: true),
                // DR-0006 escalation chain: EMPX -> contractor CTRMGR -> employee DIR. EMPX's reviewer of
                // record is DIR (the contractor is skipped); DIR is an employee manager, CTRMGR a contractor.
                Snap.Person("DIR", "BU01"),
                Snap.Person("CTRMGR", "BU01", managerExternalRef: "DIR", employeeType: "Contractor"),
                Snap.Person("EMPX", "BU01", managerExternalRef: "CTRMGR"),
                // Capability-less direct manager: EMPADM reports directly to ADMIN (a Platform Admin, which
                // holds an explicit grant and has NO individual-read capability, FR-025). ADMIN is EMPADM's
                // reviewer of record but must still be denied read + moderation (moderation ⊆ read).
                Snap.Person("EMPADM", "BU01", managerExternalRef: "ADMIN"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var bu01 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU01");
            bu01.SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("MGR2", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("EMP2", role: "Individual"),
            Snap.SeedUser("EMP3", role: "Individual"),
            Snap.SeedUser("LEAVE1", role: "Individual"),
            Snap.SeedUser("DIR", role: "Manager"),
            Snap.SeedUser("CTRMGR", role: "Manager"),
            Snap.SeedUser("EMPX", role: "Individual"),
            Snap.SeedUser("EMPADM", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
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
    /// <paramref name="score"/>, and submits — leaving a Submitted assessment ready for moderation.</summary>
    private async Task SubmitSelfAsync(string externalRef, int score)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        var submit = await client.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    /// <summary>The person's MOST RECENT assessment id (a person accrues one per cycle, so the
    /// carry-forward test — which spans two cycles — needs the latest, not Single).</summary>
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
        return await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.IndividualView && a.SubjectPersonId == subjectPersonId);
    }

    private static Task<HttpResponseMessage> ModerateAsync(HttpClient client, Guid assessmentId, params ModerateDecision[] decisions) =>
        client.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions));

    // === Review queue (FR-069/063) ================================================================

    [Fact]
    public async Task Reviews_lists_the_managers_active_direct_reports_with_state_and_leave_flags()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2); // EMP1 submitted; EMP2/LEAVE1 have not started

        var mgr1 = await ClientAsync("MGR1");
        var reviews = (await (await mgr1.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;

        Assert.True(reviews.IsManager);
        var refs = reviews.Reviews.Select(r => r.DisplayName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "EMP1", "EMP2", "LEAVE1" }, refs); // exactly MGR1's active directs — never EMP3 (MGR2's)

        var emp1 = reviews.Reviews.Single(r => r.DisplayName == "EMP1");
        Assert.Equal("Submitted", emp1.State);
        Assert.True(emp1.CanModerate);
        Assert.NotNull(emp1.AssessmentId);

        var leave = reviews.Reviews.Single(r => r.DisplayName == "LEAVE1");
        Assert.True(leave.OnLeave);                 // FR-069 leave-status display
        Assert.Equal("NotStarted", leave.State);
        Assert.Null(leave.AssessmentId);
    }

    [Fact]
    public async Task A_non_manager_gets_an_empty_review_queue()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var emp1 = await ClientAsync("EMP1");
        var reviews = (await (await emp1.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;

        Assert.False(reviews.IsManager);
        Assert.Empty(reviews.Reviews);
    }

    // === Member assessment read [A] (FR-008; L3 boundaries) =======================================

    [Fact]
    public async Task Manager_reads_a_direct_report_submitted_assessment_with_self_scores_and_adopt_default()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        var response = await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = (await response.Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;

        Assert.Equal("EMP1", view.DisplayName);
        Assert.Equal("Submitted", view.State);
        Assert.True(view.Editable);
        Assert.Equal(7, view.Dimensions.Count);
        Assert.All(view.Dimensions, d => Assert.Equal(2, d.SelfScore));
        Assert.All(view.Dimensions, d => Assert.Null(d.ManagerScore));         // not yet moderated
        Assert.All(view.Dimensions, d => Assert.Equal(2, d.DefaultManagerScore)); // no prior → adopt self
        Assert.All(view.Dimensions, d => Assert.Equal(4, d.Levels.Count));     // descriptors from framework data
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_member_read_writes_exactly_one_IndividualView_audit_row_per_call()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var mgr1Id = await PersonIdAsync("MGR1");

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.OK, (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment")).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(emp1Id)); // exactly one per successful call

        Assert.Equal(HttpStatusCode.OK, (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment")).StatusCode);
        Assert.Equal(2, await IndividualViewCountAsync(emp1Id)); // one MORE — one per call, not per session

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var row = await db.AuditLogs.FirstAsync(a => a.Action == AuditAction.IndividualView && a.SubjectPersonId == emp1Id);
        Assert.Equal(mgr1Id, row.ActorPersonId); // actor = the viewing manager
    }

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData("MGR2")]  // a manager of a DIFFERENT chain
    [InlineData("EMP2")]  // a peer (same manager, no reports)
    [InlineData("ADMIN")] // Platform Admin — no individual-read capability
    public async Task Reading_a_report_outside_the_callers_chain_is_404_and_writes_no_audit_row(string attacker)
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3);
        var emp1Id = await PersonIdAsync("EMP1");

        var client = await ClientAsync(attacker);
        var response = await client.GetAsync($"/api/team/members/{emp1Id}/assessment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);   // existence-leak convention
        Assert.Equal(0, await IndividualViewCountAsync(emp1Id));      // a denied read leaves NO audit trace
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task The_member_read_is_fail_closed_when_the_audit_write_fails()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var mgr1Id = await PersonIdAsync("MGR1");

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            // A service whose audit writer always fails: the IndividualView row is staged and committed
            // BEFORE the scores are returned, so the failure must fail the whole read (fail-closed, D1).
            var svc = new ManagerModerationService(
                db,
                scope.ServiceProvider.GetRequiredService<IAssessmentStore>(),
                scope.ServiceProvider.GetRequiredService<AssessmentReads>(),
                scope.ServiceProvider.GetRequiredService<ChainResolver>(),
                scope.ServiceProvider.GetRequiredService<OrgGraphLoader>(),
                scope.ServiceProvider.GetRequiredService<CycleService>(),
                new ThrowingAuditWriter(),
                scope.ServiceProvider.GetRequiredService<ErasureLedger>());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.GetMemberAssessmentAsync(mgr1Id, emp1Id));
        }

        Assert.Equal(0, await IndividualViewCountAsync(emp1Id)); // the failed audit persisted nothing
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Reading_a_report_that_has_no_assessment_row_is_404_with_no_audit()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        // EMP2 (MGR1's direct report) never started an assessment.
        var emp2Id = await PersonIdAsync("EMP2");

        var mgr1 = await ClientAsync("MGR1");
        var response = await mgr1.GetAsync($"/api/team/members/{emp2Id}/assessment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(emp2Id)); // no data viewed → no audit
    }

    // === Moderation write (FR-008/009/010/063) ====================================================

    [Fact]
    public async Task Moderation_adopts_the_self_score_by_default_and_transitions_to_moderated()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var mgr1Id = await PersonIdAsync("MGR1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        var moderate = await ModerateAsync(mgr1, assessmentId); // no explicit decisions → adopt self
        Assert.Equal(HttpStatusCode.NoContent, moderate.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
        Assert.Equal(AssessmentState.Moderated, assessment.State);
        Assert.Equal(mgr1Id, assessment.ModeratedByPersonId);

        var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentId).ToListAsync();
        Assert.Equal(7, scores.Count);
        Assert.All(scores, s => Assert.Equal(2, s.SelfScore));    // both self…
        Assert.All(scores, s => Assert.Equal(2, s.ManagerScore)); // …and manager persist (FR-010/011)
    }

    [Fact]
    public async Task Divergence_of_two_or_more_without_a_comment_is_422_and_persists_nothing()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var firstDim = view.Dimensions[0].DimensionId;

        // Self 3, manager 1 → Δ2, no comment → 422.
        var rejected = await ModerateAsync(mgr1, assessmentId, new ModerateDecision(firstDim, 1, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
            Assert.Equal(AssessmentState.Submitted, assessment.State); // still Submitted — nothing applied
        }

        // The same divergence WITH a comment is accepted.
        var accepted = await ModerateAsync(mgr1, assessmentId, new ModerateDecision(firstDim, 1, "evidence shows L1"));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Carry_forward_default_uses_the_prior_moderated_score_when_the_self_score_is_unchanged()
    {
        var (admin, fvId) = await SeedAsync();

        // Cycle N: EMP1 self-scores 2 everywhere; MGR1 moderates every dimension down to 1 (Δ1, no
        // comment needed) — so the prior MODERATED score is 1 while the prior SELF score is 2.
        var cycleN = await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentN = await AssessmentIdAsync("EMP1");
        var mgr1 = await ClientAsync("MGR1");
        var viewN = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var toOne = viewN.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, null)).ToArray();
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, assessmentN, toOne)).StatusCode);
        await admin.PostAsync($"/api/cycles/{cycleN}/close", null);

        // Cycle N+1: EMP1 self-scores 2 again (UNCHANGED from prior self). The carry-forward default must
        // be the prior MODERATED score (1), not the self-score.
        await CreateAndOpenCycleAsync(admin, fvId, "2026-09");
        await SubmitSelfAsync("EMP1", 2);
        var assessmentNext = await AssessmentIdAsync("EMP1");

        var viewNext = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        Assert.All(viewNext.Dimensions, d => Assert.Equal(2, d.PriorSelfScore));
        Assert.All(viewNext.Dimensions, d => Assert.Equal(1, d.PriorManagerScore));
        Assert.All(viewNext.Dimensions, d => Assert.Equal(1, d.DefaultManagerScore)); // FR-063 carry-forward

        // Moderating with omitted decisions carries the prior moderated score (1) forward.
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, assessmentNext)).StatusCode);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentNext).ToListAsync();
        Assert.All(scores, s => Assert.Equal(1, s.ManagerScore)); // carried forward, not adopted-self (2)
    }

    [Fact]
    public async Task Changing_the_self_score_defaults_to_adopting_it_not_carrying_forward()
    {
        var (admin, fvId) = await SeedAsync();

        var cycleN = await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentN = await AssessmentIdAsync("EMP1");
        var mgr1 = await ClientAsync("MGR1");
        var viewN = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        await ModerateAsync(mgr1, assessmentN, viewN.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, null)).ToArray());
        await admin.PostAsync($"/api/cycles/{cycleN}/close", null);

        // Cycle N+1: EMP1 CHANGES their self-score to 3 → default adopts 3 (self changed), not the prior 1.
        await CreateAndOpenCycleAsync(admin, fvId, "2026-09");
        await SubmitSelfAsync("EMP1", 3);
        var viewNext = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;

        Assert.All(viewNext.Dimensions, d => Assert.Equal(3, d.DefaultManagerScore)); // adopt the changed self
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Moderating_a_report_outside_the_callers_chain_is_404()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var assessmentId = await AssessmentIdAsync("EMP1");

        // MGR2 manages a different chain — must not be able to moderate MGR1's report (existence-leak 404).
        var mgr2 = await ClientAsync("MGR2");
        Assert.Equal(HttpStatusCode.NotFound, (await ModerateAsync(mgr2, assessmentId)).StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
        Assert.Equal(AssessmentState.Submitted, assessment.State); // untouched
    }

    [Fact]
    public async Task Re_moderating_an_already_moderated_assessment_is_409()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, assessmentId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ModerateAsync(mgr1, assessmentId)).StatusCode);
    }

    [Fact]
    public async Task Moderating_an_unsubmitted_assessment_is_409()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // EMP2 saves a partial draft (InProgress) but never submits.
        var emp2 = await ClientAsync("EMP2");
        var form = (await (await emp2.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await emp2.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(new[] { new ScoreEntry(form.Dimensions[0].DimensionId, 1, null) }));
        var assessmentId = await AssessmentIdAsync("EMP2");

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.Conflict, (await ModerateAsync(mgr1, assessmentId)).StatusCode);
    }

    [Fact]
    public async Task Moderating_an_unknown_assessment_id_is_404()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.NotFound, (await ModerateAsync(mgr1, Guid.NewGuid())).StatusCode);
    }

    // === DR-0006 contractor-manager escalation (reviewer of record) ===============================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Contractor_manager_review_escalates_to_the_employee_reviewer_of_record()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMPX", 2); // EMPX -> contractor CTRMGR -> employee DIR
        var empxId = await PersonIdAsync("EMPX");
        var dirId = await PersonIdAsync("DIR");
        var assessmentId = await AssessmentIdAsync("EMPX");

        // (a) The contractor DIRECT manager sees nothing and cannot act — and leaves no audit trace.
        var ctr = await ClientAsync("CTRMGR");
        var ctrReviews = (await (await ctr.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.False(ctrReviews.IsManager);
        Assert.DoesNotContain(ctrReviews.Reviews, r => r.DisplayName == "EMPX");
        Assert.Equal(HttpStatusCode.NotFound, (await ctr.GetAsync($"/api/team/members/{empxId}/assessment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ModerateAsync(ctr, assessmentId)).StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(empxId));

        // (b) The employee REVIEWER OF RECORD (DIR) sees the item and can GET (one audit row) + moderate.
        var dir = await ClientAsync("DIR");
        var dirReviews = (await (await dir.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.True(dirReviews.IsManager);
        var item = dirReviews.Reviews.Single(r => r.DisplayName == "EMPX");
        Assert.Equal("Submitted", item.State);
        Assert.DoesNotContain(dirReviews.Reviews, r => r.DisplayName == "CTRMGR"); // contractor is never a queue item
        Assert.Equal(HttpStatusCode.OK, (await dir.GetAsync($"/api/team/members/{empxId}/assessment")).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(empxId));
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(dir, assessmentId)).StatusCode);

        // (c) The item did NOT dead-end — it is Moderated, recorded against DIR.
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
        Assert.Equal(AssessmentState.Moderated, assessment.State);
        Assert.Equal(dirId, assessment.ModeratedByPersonId);
    }

    // === L3: moderation ⊆ read — a capability-less direct manager is denied (score-oracle leak) ====

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_capability_less_direct_manager_cannot_read_moderate_or_see_a_queue()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMPADM", 3); // EMPADM reports directly to ADMIN (a Platform Admin)
        var empAdmId = await PersonIdAsync("EMPADM");
        var assessmentId = await AssessmentIdAsync("EMPADM");

        // ADMIN is EMPADM's reviewer of record, but a Platform Admin has NO individual-read capability
        // (FR-025 clause 2). All three surfaces must deny — matching the pinned gateway behaviour.
        var adminClient = await ClientAsync("ADMIN");

        // Queue: empty / not-a-manager (must not leak EMPADM's name + assessment state).
        var queue = (await (await adminClient.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.False(queue.IsManager);
        Assert.DoesNotContain(queue.Reviews, r => r.DisplayName == "EMPADM");

        // Member read: 404, no audit row (the score oracle never opens).
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync($"/api/team/members/{empAdmId}/assessment")).StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(empAdmId));

        // Moderation: 404 (existence-leak), assessment untouched.
        Assert.Equal(HttpStatusCode.NotFound, (await ModerateAsync(adminClient, assessmentId, new ModerateDecision(Guid.NewGuid(), 0, null))).StatusCode);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
        Assert.Equal(AssessmentState.Submitted, assessment.State);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_divergence_comment_required_422_body_leaks_no_score_values()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3); // self-score 3 — must never appear in the error body
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;

        // Manager score 0 vs self 3 → Δ3, no comment → 422. The Problem body must reveal neither the
        // self-score nor the exact divergence (the old "(self X, manager Y)" oracle is scrubbed).
        var response = await ModerateAsync(mgr1, assessmentId, new ModerateDecision(view.Dimensions[0].DimensionId, 0, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("(self", body, StringComparison.OrdinalIgnoreCase); // the leak pattern is gone
        Assert.DoesNotContain("manager 0", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("comment", body, StringComparison.OrdinalIgnoreCase);      // still tells the manager what to do
    }

    // === FR-063 + FR-009: a carried-forward DIVERGENT default requires a fresh comment (Q-019) =====

    [Fact]
    public async Task A_carried_forward_divergent_default_flags_comment_required_and_an_empty_accept_is_422()
    {
        var (admin, fvId) = await SeedAsync();

        // Cycle N: EMP1 self 3; MGR1 moderates every dimension to 1 WITH a comment (Δ2). Prior
        // moderated=1, prior self=3.
        var cycleN = await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        await SubmitSelfAsync("EMP1", 3);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentN = await AssessmentIdAsync("EMP1");
        var mgr1 = await ClientAsync("MGR1");
        var viewN = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        await ModerateAsync(mgr1, assessmentN,
            viewN.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "moderated to L1")).ToArray());
        await admin.PostAsync($"/api/cycles/{cycleN}/close", null);

        // Cycle N+1: EMP1 self 3 again (UNCHANGED) → carry-forward default = prior moderated 1 → Δ2.
        await CreateAndOpenCycleAsync(admin, fvId, "2026-09");
        await SubmitSelfAsync("EMP1", 3);
        var assessmentNext = await AssessmentIdAsync("EMP1");

        var viewNext = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        Assert.All(viewNext.Dimensions, d => Assert.Equal(1, d.DefaultManagerScore));
        Assert.All(viewNext.Dimensions, d => Assert.True(d.DefaultCommentRequired)); // GET flags it up front

        // An "accept all defaults" PUT with no comments is rejected 422 — GET and PUT agree, FR-009 intact.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ModerateAsync(mgr1, assessmentNext)).StatusCode);

        // Supplying a fresh comment accepts the carried default.
        var accepted = await ModerateAsync(mgr1, assessmentNext,
            viewNext.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "carried forward, still L1")).ToArray());
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    // === Q-017a post-close submission lock ========================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Post_close_moderation_is_locked_423_unless_a_late_override_exists()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.Locked, (await ModerateAsync(mgr1, assessmentId)).StatusCode);

        // A late override for the SUBJECT reopens the moderation window (the same lock the self path uses).
        var granted = await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, assessmentId)).StatusCode);
    }

    [Fact]
    public async Task Queue_CanModerate_honours_a_post_close_late_override_matching_the_write_path()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2); // EMP1 submitted
        await SubmitSelfAsync("EMP2", 2); // EMP2 submitted too (no override → stays un-moderatable post-close)
        var emp1Id = await PersonIdAsync("EMP1");
        var mgr1 = await ClientAsync("MGR1");

        // While Open, both submitted reports are moderatable in the queue.
        var openQueue = (await (await mgr1.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        Assert.True(openQueue.Reviews.Single(r => r.DisplayName == "EMP1").CanModerate);
        Assert.True(openQueue.Reviews.Single(r => r.DisplayName == "EMP2").CanModerate);

        // Close the cycle, then grant EMP1 (only) a late override.
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id));

        // The queue must now match the write path: EMP1 (with override) shows moderatable, EMP2 does not —
        // and the PUT agrees for both.
        var closedQueue = (await (await mgr1.GetAsync("/api/team/reviews")).Content.ReadFromJsonAsync<TeamReviewsResponse>())!;
        var emp1Item = closedQueue.Reviews.Single(r => r.DisplayName == "EMP1");
        var emp2Item = closedQueue.Reviews.Single(r => r.DisplayName == "EMP2");
        Assert.True(emp1Item.CanModerate);
        Assert.False(emp2Item.CanModerate);
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, emp1Item.AssessmentId!.Value)).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await ModerateAsync(mgr1, emp2Item.AssessmentId!.Value)).StatusCode);
    }

    // === Optimistic concurrency on the score of record (xmin token) ===============================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Concurrent_moderations_never_both_win_exactly_one_ScoreChange_row_and_never_500()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        // Two managers can't both be the reviewer, so the race is one reviewer firing twice in parallel.
        var mgr1a = await ClientAsync("MGR1");
        var mgr1b = await ClientAsync("MGR1");
        var results = await Task.WhenAll(ModerateAsync(mgr1a, assessmentId), ModerateAsync(mgr1b, assessmentId));

        Assert.All(results, r => Assert.True(
            r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Conflict,
            $"a moderation race must resolve to 204/409, never a 500 — got {r.StatusCode}"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent); // exactly one logical winner

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessment = await db.Set<Assessment>().SingleAsync(a => a.Id == assessmentId);
        Assert.Equal(AssessmentState.Moderated, assessment.State);
        // The score of record was written exactly once — the loser's ScoreChange row rolled back with it.
        var scoreChanges = await db.AuditLogs
            .CountAsync(a => a.Action == AuditAction.ScoreChange && a.SubjectPersonId == emp1Id);
        Assert.Equal(1, scoreChanges);
    }

    // === ScoreChange audit on moderation (Q-018) ==================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_successful_moderation_writes_exactly_one_ScoreChange_audit_row()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2);
        var emp1Id = await PersonIdAsync("EMP1");
        var mgr1Id = await PersonIdAsync("MGR1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        Assert.Equal(HttpStatusCode.NoContent, (await ModerateAsync(mgr1, assessmentId)).StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var scoreChanges = await db.AuditLogs
            .Where(a => a.Action == AuditAction.ScoreChange && a.SubjectPersonId == emp1Id)
            .ToListAsync();
        Assert.Single(scoreChanges);
        Assert.Equal(mgr1Id, scoreChanges[0].ActorPersonId);
    }

    // === Individual result view (FR-012) ==========================================================

    [Fact]
    public async Task Result_is_404_before_moderation_and_returns_manager_scores_and_divergence_after()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        // Before moderation → 404 (no result of record yet).
        var emp1 = await ClientAsync("EMP1");
        Assert.Equal(HttpStatusCode.NotFound, (await emp1.GetAsync("/api/me/assessment/result")).StatusCode);

        // MGR1 moderates every dimension down to 1 (Δ2 → comment required).
        var mgr1 = await ClientAsync("MGR1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        await ModerateAsync(mgr1, assessmentId,
            view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "moderated to L1")).ToArray());

        // After moderation → 200 with per-dimension manager scores, comments, and divergence (FR-012).
        var result = await emp1.GetAsync("/api/me/assessment/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = (await result.Content.ReadFromJsonAsync<AssessmentResultResponse>())!;
        Assert.Equal("Moderated", body.State);
        Assert.Equal(7, body.Dimensions.Count);
        Assert.All(body.Dimensions, d => Assert.Equal(3, d.SelfScore));
        Assert.All(body.Dimensions, d => Assert.Equal(1, d.ManagerScore));
        Assert.All(body.Dimensions, d => Assert.Equal(2, d.Divergence));       // |3 − 1|
        Assert.All(body.Dimensions, d => Assert.Equal("moderated to L1", d.ManagerComment));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task The_result_endpoint_only_ever_returns_the_callers_own_data()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3);
        var emp1Id = await PersonIdAsync("EMP1");
        var assessmentId = await AssessmentIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        await ModerateAsync(mgr1, assessmentId); // EMP1 now moderated

        // EMP2 (a peer) hits the same endpoint — there is no person id to supply, so they only ever see
        // their OWN result, which does not exist → 404. EMP1's moderated data is never exposed to them.
        var emp2 = await ClientAsync("EMP2");
        Assert.Equal(HttpStatusCode.NotFound, (await emp2.GetAsync("/api/me/assessment/result")).StatusCode);
    }
}
