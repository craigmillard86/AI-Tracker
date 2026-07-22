using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-8 acceptance criteria for the self-assessment endpoints (contracts/api.md "Self scope";
/// FR-007/062/066). Exercises the whole path end-to-end against the seeded framework + a real Open
/// cycle: form data from framework content, partial upsert + restore (User Story 1 scenario 3),
/// submit + completeness, the post-close submission lock (Q-017a / HAP-7 handoff), pre-population
/// (FR-062), and — Category=PrivacyReporting — that the self endpoints expose no cross-person surface
/// and write no IndividualView audit row for a self view.
///
/// <para>Fixture: BU01 onboarded; ADMIN (Platform Admin), MGR1 (Manager of EMP1), EMP1/EMP2
/// (Individuals). The framework seeds seven dimensions with 0–3 descriptors.</para>
/// </summary>
[Collection("hap-db")]
public sealed class AssessmentEndpointsTests
{
    private const int DimensionCount = 7;

    private readonly HapApiFactory _factory;

    public AssessmentEndpointsTests(HapApiFactory factory) => _factory = factory;

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
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
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
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("EMP2", role: "Individual"),
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

    private static async Task<SelfAssessmentResponse> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me/assessment");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
    }

    private static Task<HttpResponseMessage> PutScoresAsync(HttpClient client, IEnumerable<ScoreEntry> scores) =>
        client.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(scores.ToList()));

    private static IReadOnlyList<ScoreEntry> ScoreAll(SelfAssessmentResponse form, int score, string? evidence = null) =>
        form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, evidence)).ToList();

    // --- FR-007 / FR-066: form content ------------------------------------------------------------

    [Fact]
    public async Task Get_returns_seven_dimensions_with_descriptors_prior_null_and_the_purpose_key()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        var form = await GetAsync(emp1);

        Assert.Equal(DimensionCount, form.DimensionCount);
        Assert.Equal(DimensionCount, form.Dimensions.Count);
        Assert.False(form.Submitted);
        Assert.Equal("assessment.purposeLimitation", form.PurposeLimitationKey); // FR-066 copy key
        foreach (var dim in form.Dimensions)
        {
            Assert.False(string.IsNullOrWhiteSpace(dim.Name));
            Assert.Equal(4, dim.Levels.Count); // 0–3 descriptors, from framework data (FR-001)
            Assert.All(dim.Levels, l => Assert.False(string.IsNullOrWhiteSpace(l.DescriptorText)));
            Assert.Null(dim.SelfScore);  // nothing entered yet
            Assert.Null(dim.PriorScore); // no prior cycle
        }
    }

    // --- FR-007 / User Story 1 scenario 3: partial upsert + restore -------------------------------

    [Fact]
    public async Task Put_upserts_partial_progress_and_a_later_get_restores_the_in_progress_values()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        var form = await GetAsync(emp1);
        var five = form.Dimensions.Take(5)
            .Select((d, i) => new ScoreEntry(d.DimensionId, i % 4, i == 0 ? "note" : null))
            .ToList();

        var put = await PutScoresAsync(emp1, five);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var restored = await GetAsync(emp1);
        var scored = restored.Dimensions.Where(d => d.SelfScore is not null).ToList();
        Assert.Equal(5, scored.Count); // 5 of 7 restored (scenario 3)
        var first = restored.Dimensions.Single(d => d.DimensionId == five[0].DimensionId);
        Assert.Equal(0, first.SelfScore);
        Assert.Equal("note", first.SelfEvidence);
        Assert.False(restored.Submitted);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task Put_rejects_a_score_outside_0_to_3_with_422(int badScore)
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);

        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(form.Dimensions[0].DimensionId, badScore, null) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
    }

    // --- FR-007: submit + completeness ------------------------------------------------------------

    [Fact]
    public async Task Submit_requires_all_dimensions_scored_then_transitions_to_submitted()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);

        // Only 5 of 7 scored → submit is 422.
        await PutScoresAsync(emp1, form.Dimensions.Take(5).Select(d => new ScoreEntry(d.DimensionId, 2, null)));
        var incomplete = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);

        // Score the rest → submit succeeds.
        await PutScoresAsync(emp1, ScoreAll(form, 2));
        var submit = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);

        var after = await GetAsync(emp1);
        Assert.True(after.Submitted);
    }

    [Fact]
    public async Task A_score_write_after_submit_returns_409()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);

        await PutScoresAsync(emp1, ScoreAll(form, 1));
        await emp1.PostAsync("/api/me/assessment/submit", null);

        var write = await PutScoresAsync(emp1, new[] { new ScoreEntry(form.Dimensions[0].DimensionId, 3, null) });
        Assert.Equal(HttpStatusCode.Conflict, write.StatusCode);

        var resubmit = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, resubmit.StatusCode);
    }

    // --- Q-017a submission lock (HAP-7 handoff) ---------------------------------------------------

    [Fact]
    public async Task Submit_after_close_is_locked_423_and_a_late_override_reopens_it()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);
        await PutScoresAsync(emp1, ScoreAll(form, 2)); // fully scored, not yet submitted

        // Close the cycle → the submit path must reject with 423 Locked.
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        var locked = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);

        // A score write is equally locked post-close.
        var lockedWrite = await PutScoresAsync(emp1, new[] { new ScoreEntry(form.Dimensions[0].DimensionId, 3, null) });
        Assert.Equal(HttpStatusCode.Locked, lockedWrite.StatusCode);

        // Grant EMP1 a late override → submission is accepted again.
        Guid emp1Id;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            emp1Id = (await db.People.SingleAsync(p => p.ExternalRef == "EMP1")).Id;
        }
        var granted = await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        var submit = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    // --- FR-062 pre-population ---------------------------------------------------------------------

    [Fact]
    public async Task Get_pre_populates_with_the_prior_cycle_scores_cycle_n_plus_1_shows_cycle_n_values()
    {
        var (admin, fvId) = await SeedAsync();

        // Cycle N: EMP1 scores every dimension 2 and submits, then the cycle closes.
        var cycleN = await CreateAndOpenCycleAsync(admin, fvId, name: "2026-08");
        var emp1 = await ClientAsync("EMP1");
        var formN = await GetAsync(emp1);
        await PutScoresAsync(emp1, ScoreAll(formN, 2));
        await emp1.PostAsync("/api/me/assessment/submit", null);
        await admin.PostAsync($"/api/cycles/{cycleN}/close", null);

        // Cycle N+1 opens: EMP1's form pre-populates PriorScore=2, SelfScore still null (unconfirmed).
        await CreateAndOpenCycleAsync(admin, fvId, name: "2026-09");
        var formNext = await GetAsync(emp1);

        Assert.All(formNext.Dimensions, d => Assert.Equal(2, d.PriorScore));
        Assert.All(formNext.Dimensions, d => Assert.Null(d.SelfScore));
        Assert.False(formNext.Submitted);
    }

    // === Category=PrivacyReporting — the sacred seam (§9.3) =======================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Self_endpoints_expose_no_cross_person_surface_each_caller_only_reaches_their_own_data()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // EMP1 enters scores.
        var emp1 = await ClientAsync("EMP1");
        var emp1Form = await GetAsync(emp1);
        await PutScoresAsync(emp1, ScoreAll(emp1Form, 3));

        // MGR1 hits the SAME endpoint — there is no person id to supply — and sees only their OWN
        // (empty) assessment, never EMP1's. The self endpoints derive the subject from the session, so
        // no cross-person read is even expressible here.
        var mgr = await ClientAsync("MGR1");
        var mgrForm = await GetAsync(mgr);
        Assert.All(mgrForm.Dimensions, d => Assert.Null(d.SelfScore));

        // EMP1's own data is still intact and isolated.
        var emp1Again = await GetAsync(emp1);
        Assert.All(emp1Again.Dimensions, d => Assert.Equal(3, d.SelfScore));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_self_view_writes_no_IndividualView_audit_row()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        var form = await GetAsync(emp1);          // self view
        await PutScoresAsync(emp1, ScoreAll(form, 1)); // self write
        await GetAsync(emp1);                      // another self view

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1Id = (await db.People.SingleAsync(p => p.ExternalRef == "EMP1")).Id;
        var individualViewRows = await db.AuditLogs
            .CountAsync(a => a.Action == AuditAction.IndividualView && a.SubjectPersonId == emp1Id);

        Assert.Equal(0, individualViewRows); // viewing your own data is not audited (contracts/api.md)
    }

    // === Invitation gating (FR-002/FR-005; panel round-1 BLOCKING 1) ==============================
    // A self-assessment may only be read/written by an INVITED, non-excluded participant of the cycle,
    // so an excluded contractor or a not-onboarded-BU person cannot land an Assessment row that every
    // downstream rollup/Harris consumer would then ingest (story US1 precondition: "an INVITED
    // individual"). Fixture adds a BU01 contractor (excluded) and a BU02 (not onboarded) person.

    private async Task<HttpClient> SeedWithExclusionsAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("CTR1", "BU01", managerExternalRef: "MGR1", employeeType: "Contractor"),
                Snap.Person("EMP_BU2", "BU02"), // BU02 is never onboarded → excluded-NotOnboarded
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
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("CTR1", role: "Individual"),
            Snap.SeedUser("EMP_BU2", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        await CreateAndOpenCycleAsync(admin, seed!.VersionId);
        return admin;
    }

    private async Task<bool> HasAssessmentRowAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        return await db.Set<Assessment>().AnyAsync(a => a.PersonId == personId);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task An_excluded_contractor_cannot_read_or_land_a_self_assessment_row()
    {
        await SeedWithExclusionsAsync();
        var ctr = await ClientAsync("CTR1");

        // Read is 404 (non-participant), and neither write path may land a row.
        Assert.Equal(HttpStatusCode.NotFound, (await ctr.GetAsync("/api/me/assessment")).StatusCode);
        var put = await PutScoresAsync(ctr, new[] { new ScoreEntry(Guid.NewGuid(), 2, null) });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ctr.PostAsync("/api/me/assessment/submit", null)).StatusCode);

        Assert.False(await HasAssessmentRowAsync("CTR1")); // no row landed for the excluded contractor
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_not_onboarded_bu_person_cannot_land_a_self_assessment_row()
    {
        await SeedWithExclusionsAsync();
        var emp = await ClientAsync("EMP_BU2");

        var put = await PutScoresAsync(emp, new[] { new ScoreEntry(Guid.NewGuid(), 1, null) });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.False(await HasAssessmentRowAsync("EMP_BU2"));
    }

    [Fact]
    public async Task An_invited_person_in_the_same_cycle_can_still_assess()
    {
        await SeedWithExclusionsAsync();
        var emp1 = await ClientAsync("EMP1");

        var form = await GetAsync(emp1); // 200 for the invited participant
        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(form.Dimensions[0].DimensionId, 2, null) });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.True(await HasAssessmentRowAsync("EMP1"));
    }

    // === Dimension-membership validation (panel round-1 BLOCKING 2) ================================

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData(false)] // a random, unknown dimension id
    [InlineData(true)]  // the Guid.Empty edge case (must not slip past a FirstOrDefault sentinel)
    public async Task Put_rejects_a_dimension_not_in_the_cycle_framework_with_a_clean_422_not_500(bool useEmptyGuid)
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        var badDimension = useEmptyGuid ? Guid.Empty : Guid.NewGuid();
        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(badDimension, 2, null) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode); // clean 422, never a 500
        Assert.False(await HasAssessmentRowAsync("EMP1")); // nothing persisted
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Put_rejects_the_same_dimension_appearing_twice_in_one_payload_422()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);
        var dim = form.Dimensions[0].DimensionId;

        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(dim, 1, null), new ScoreEntry(dim, 2, null) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
    }

    // === Editable flag (panel round-1 advisory: read-only up front) ================================

    [Fact]
    public async Task Get_marks_the_form_editable_when_open_and_read_only_when_closed_without_an_override()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        Assert.True((await GetAsync(emp1)).Editable); // open cycle, not submitted → editable

        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        Assert.False((await GetAsync(emp1)).Editable); // closed, no override → read-only
    }
}
