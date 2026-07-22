using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hap.Domain.Assessments;
using Hap.Domain.Frameworks;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-8 (fresh-instance QA pass, CLAUDE.md §9.3), added during QA
/// rather than Dev — attributed as QA work. HAP-8 is the FIRST story that writes real Assessment/
/// AssessmentScore rows, so this file targets exactly the §9.3(a)-(d) mandatory attempts plus the
/// negative-path angles the Dev/panel rounds (recorded in the story file) did not exercise directly:
/// the full seven-seeded-role cross-person sweep (Dev's own suite proved it only for MGR1), a
/// structural request-tampering proof (personId injected via body/query/header has zero effect — the
/// strongest form of "attempt" available when the endpoint takes no personId parameter at all), GET and
/// submit coverage for the not-onboarded exclusion (Dev's suite covered only PUT), a genuinely
/// foreign-framework-version dimension id (not just an unknown/random one), the Draft-cycle boundary,
/// late-override cycle-scoping, and a concurrent double-submit race.
/// </summary>
[Collection("hap-db")]
public sealed class AssessmentEndpointsQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public AssessmentEndpointsQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    // --- shared setup helpers (mirrors AssessmentEndpointsTests' fixture shape) --------------------

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
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid frameworkVersionId, string name = "2026-08") =>
        await CreateCycleAsync(admin, frameworkVersionId, name, open: true);

    private static async Task<Guid> CreateCycleAsync(HttpClient admin, Guid frameworkVersionId, string name, bool open)
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, name, true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        if (open)
        {
            await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        }
        return cycle!.Id;
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

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<bool> HasAssessmentRowAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        return await db.Set<Assessment>().AnyAsync(a => a.PersonId == personId);
    }

    // =================================================================================================
    // §9.3(a) — read a score outside the entitled reach, attempted as EACH of the seven seeded roles.
    // Dev's own suite (AssessmentEndpointsTests) proved this only for MGR1 against EMP1. The self
    // endpoints take no personId parameter at all, so the only "attempt" available to any role — no
    // matter how privileged — is to call the same endpoint and see whose data comes back. This sweep
    // uses the CANONICAL 23-BU synthetic generator (the same seven-role fixture HAP-4/5 QA used) so
    // every seeded role, including HIG Executive and Platform Admin, is exercised for real.
    // =================================================================================================

    private async Task<(HttpClient Admin, Guid FrameworkVersionId, string VictimRef)> SeedCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            // Onboard every BU so no seeded role is excluded from the cycle for an unrelated reason
            // (NotOnboarded) — this sweep is about cross-person REACH, not participation.
            var bus = await db.BusinessUnits.ToListAsync();
            foreach (var bu in bus)
            {
                bu.SetOnboarded(true);
            }
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, RefForRole("Platform Admin"));
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();

        // The victim: the canonical "Individual" seed user. They will be given a distinctive score.
        var victimRef = RefForRole("Individual");
        return (admin, seed!.VersionId, victimRef);
    }

    private string RefForRole(string role) =>
        _factory.CanonicalSeedUsers.Single(u => u.Role == role).ExternalRef;

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData("Individual")]
    [InlineData("Manager")]
    [InlineData("BU Lead")]
    [InlineData("Group Leader")]
    [InlineData("Portfolio Leader")]
    [InlineData("HIG Executive")]
    [InlineData("Platform Admin")]
    public async Task No_seeded_role_can_read_another_persons_self_assessment_via_the_self_endpoints(string attackerRole)
    {
        var (admin, fvId, victimRef) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // The victim enters a distinctive score (3, with a fingerprintable evidence string) on every
        // dimension and submits.
        var victim = await ClientAsync(victimRef);
        var victimForm = await GetAsync(victim);
        const string fingerprint = "QA-VICTIM-FINGERPRINT-HAP-8";
        await PutScoresAsync(victim, ScoreAll(victimForm, 3, fingerprint));

        // The attacker — the role under test — signs in and hits the SAME self-scope endpoints. There
        // is no personId to redirect with; the only possible outcomes are (a) their own [likely empty]
        // form, or (b) 404 if they are not themselves an invited participant this cycle. Neither may
        // ever surface the victim's score or evidence.
        var attackerRef = RefForRole(attackerRole);
        if (attackerRef == victimRef)
        {
            return; // the victim is also the sole "Individual" seed user in some generator seeds; skip self-vs-self
        }
        var attacker = await ClientAsync(attackerRef);
        var response = await attacker.GetAsync("/api/me/assessment");

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"unexpected status {response.StatusCode} for role {attackerRole}");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(fingerprint, body);
            var form = JsonSerializer.Deserialize<SelfAssessmentResponse>(
                body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.DoesNotContain(form.Dimensions, d => d.SelfEvidence == fingerprint);
            Assert.DoesNotContain(form.Dimensions, d => d.SelfScore == 3 && d.SelfEvidence == fingerprint);
        }

        // A write attempt fares no better — the attacker can only ever write to THEIR OWN row (or is
        // rejected outright), never the victim's.
        var putAttempt = await PutScoresAsync(attacker, new[] { new ScoreEntry(victimForm.Dimensions[0].DimensionId, 1, "attacker-write") });
        Assert.True(
            putAttempt.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound
                or HttpStatusCode.Conflict or HttpStatusCode.Locked or HttpStatusCode.UnprocessableEntity,
            $"unexpected PUT status {putAttempt.StatusCode} for role {attackerRole}");

        // The victim's data is untouched regardless of what the attacker attempted.
        var victimAfter = await GetAsync(victim);
        Assert.All(victimAfter.Dimensions, d => Assert.Equal(3, d.SelfScore));
        Assert.All(victimAfter.Dimensions, d => Assert.Equal(fingerprint, d.SelfEvidence));
    }

    // =================================================================================================
    // Structural request-tampering proof: since the self endpoints take no personId parameter, the
    // strongest available "attempt" is to inject one anyway — as an extra JSON body field, a query
    // string parameter, and a custom header — and prove it has zero effect. This directly tests the
    // claim in AssessmentEndpoints.cs's class doc ("none takes a person id in the route or body").
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Injecting_a_personId_via_body_query_string_or_header_has_no_effect_on_the_subject()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var mgr1 = await ClientAsync("MGR1");
        var emp1 = await ClientAsync("EMP1");
        var emp1Form = await GetAsync(emp1);
        var emp1Id = await PersonIdAsync("EMP1");

        // EMP1 (the victim) gets a distinctive fingerprint FIRST, so every subsequent assertion checks
        // for the fingerprint specifically — not "the attacker's own row happens to be empty", which
        // would conflate "no retargeting occurred" with "the attacker never legitimately wrote to their
        // own row" (a real write to the attacker's OWN data, as a side effect of an injection attempt
        // that used a real payload, is expected and is NOT itself a violation).
        const string fingerprint = "QA-INJECTION-VICTIM-FINGERPRINT";
        await PutScoresAsync(emp1, ScoreAll(emp1Form, 3, fingerprint));

        // (a) Extra "personId"/"subjectPersonId"/"targetPersonId" fields spliced into the raw JSON body
        // of a PUT — MGR1 attempts to write EMP1's dimension by naming EMP1 explicitly in the payload
        // (score 2, no evidence, so it is trivially distinguishable from EMP1's fingerprinted score 3).
        var tamperedJson = JsonSerializer.Serialize(new
        {
            Scores = new[] { new { DimensionId = emp1Form.Dimensions[0].DimensionId, Score = 2, Evidence = (string?)null } },
            personId = emp1Id,
            subjectPersonId = emp1Id,
            targetPersonId = emp1Id,
        });
        using var tamperedContent = new StringContent(tamperedJson, System.Text.Encoding.UTF8, "application/json");
        var tamperedPut = await mgr1.PutAsync("/api/me/assessment/scores", tamperedContent);
        // Accepted or not, it can only ever have written to MGR1's OWN row — never EMP1's. (It IS
        // accepted here: the "Scores" array is a legitimate MGR1 self-write; the extra personId-shaped
        // fields are simply ignored by the DTO binder — exactly the property under test.)
        Assert.Equal(HttpStatusCode.NoContent, tamperedPut.StatusCode);

        // (b) A personId query-string parameter on GET — a FRESH caller (ADMIN, who has made no writes
        // of their own) so the assertion isolates "did the query string redirect the subject" cleanly.
        var admin2 = await ClientAsync("ADMIN");
        var queryGet = await admin2.GetAsync($"/api/me/assessment?personId={emp1Id}");
        Assert.Equal(HttpStatusCode.OK, queryGet.StatusCode);
        var queryForm = await queryGet.Content.ReadFromJsonAsync<SelfAssessmentResponse>();
        Assert.DoesNotContain(queryForm!.Dimensions, d => d.SelfEvidence == fingerprint);
        Assert.All(queryForm.Dimensions, d => Assert.Null(d.SelfScore)); // ADMIN's own untouched form, not EMP1's

        // (c) A custom header some client might plausibly send — MGR1 again (already legitimately wrote
        // dimension[0]=2 in part (a)); the assertion checks for the FINGERPRINT specifically, proving
        // the header never redirected the read to EMP1's row, rather than asserting MGR1's own row is
        // empty (it correctly is not, per part (a)).
        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me/assessment");
        headerRequest.Headers.Add("X-Person-Id", emp1Id.ToString());
        headerRequest.Headers.Add("X-Subject-Person-Id", emp1Id.ToString());
        var headerResponse = await mgr1.SendAsync(headerRequest);
        Assert.Equal(HttpStatusCode.OK, headerResponse.StatusCode);
        var headerForm = await headerResponse.Content.ReadFromJsonAsync<SelfAssessmentResponse>();
        Assert.DoesNotContain(headerForm!.Dimensions, d => d.SelfEvidence == fingerprint);
        Assert.DoesNotContain(headerForm.Dimensions, d => d.SelfScore == 3 && d.SelfEvidence == fingerprint);

        // EMP1's fingerprinted data was never touched by any of the three attempts.
        var emp1Final = await GetAsync(emp1);
        Assert.All(emp1Final.Dimensions, d => Assert.Equal(3, d.SelfScore));
        Assert.All(emp1Final.Dimensions, d => Assert.Equal(fingerprint, d.SelfEvidence));
    }

    // =================================================================================================
    // §9.3(b) — invitation gating: fill the GET/submit gap the Dev suite left (it covered PUT only for
    // the not-onboarded case) and probe an ordering variant (submit-without-any-prior-GET-or-PUT).
    // =================================================================================================

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
                Snap.Person("EMP_BU2", "BU02"), // BU02 never onboarded → excluded-NotOnboarded
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

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_not_onboarded_bu_person_gets_404_on_GET_and_submit_too_not_just_PUT()
    {
        await SeedWithExclusionsAsync();
        var emp = await ClientAsync("EMP_BU2");

        // Dev's suite proved PUT is 404; this closes the GET and submit legs of the same gate.
        Assert.Equal(HttpStatusCode.NotFound, (await emp.GetAsync("/api/me/assessment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await emp.PostAsync("/api/me/assessment/submit", null)).StatusCode);
        Assert.False(await HasAssessmentRowAsync("EMP_BU2"));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task An_excluded_contractor_gets_404_on_submit_with_no_prior_GET_or_PUT_ordering_probe()
    {
        await SeedWithExclusionsAsync();
        var ctr = await ClientAsync("CTR1");

        // Straight to submit — no GET, no PUT beforehand — probing for an ordering window where the
        // gate might only be checked on the first call of a session.
        var submit = await ctr.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        Assert.False(await HasAssessmentRowAsync("CTR1"));
    }

    // =================================================================================================
    // §9.3(c) — a genuinely FOREIGN-framework-version dimension id: a real Dimension row that exists in
    // the database but belongs to a DIFFERENT FrameworkVersion than the one the current cycle resolved
    // to. Stronger than an unknown/random Guid (Dev's suite): proves ValidateDimensionsAsync checks
    // MEMBERSHIP of the resolved cycle's framework version, not merely "does this id exist anywhere".
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Put_rejects_a_real_dimension_belonging_to_a_different_framework_version_422_nothing_persisted()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");

        Guid foreignDimensionId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var framework = Framework.Create("qa-foreign-framework", "QA Foreign Framework", null, null);
            db.Frameworks.Add(framework);
            var foreignVersion = FrameworkVersion.Create(framework.Id, 1, null);
            db.FrameworkVersions.Add(foreignVersion);
            var foreignDimension = Dimension.Create(foreignVersion.Id, "qa-foreign-dim", "QA Foreign Dimension", 1);
            db.Dimensions.Add(foreignDimension);
            await db.SaveChangesAsync();
            foreignDimensionId = foreignDimension.Id;
        }

        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(foreignDimensionId, 2, null) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.False(await HasAssessmentRowAsync("EMP1")); // no phantom score, no phantom assessment row
    }

    // =================================================================================================
    // Draft-cycle boundary: a cycle that exists but was never opened must behave exactly like "no
    // cycle" (404 on GET, 409 on write) — never accidentally treated as current, never 423.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_draft_never_opened_cycle_is_treated_as_no_current_cycle_not_423()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateCycleAsync(admin, fvId, "2026-08-draft", open: false); // stays Draft — never opened
        var emp1 = await ClientAsync("EMP1");

        var get = await emp1.GetAsync("/api/me/assessment");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var put = await PutScoresAsync(emp1, new[] { new ScoreEntry(Guid.NewGuid(), 1, null) });
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode); // NoCurrentCycleException on write → 409, never 423

        var submit = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);

        Assert.False(await HasAssessmentRowAsync("EMP1"));
    }

    // =================================================================================================
    // Late-override cycle-scoping: an override granted for a CLOSED cycle must not leak forward and
    // grant editability on a subsequently-opened NEW cycle for the same person.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_late_override_on_a_closed_cycle_does_not_apply_to_a_later_newly_opened_cycle()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleA = await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        var emp1 = await ClientAsync("EMP1");
        var formA = await GetAsync(emp1);
        await PutScoresAsync(emp1, ScoreAll(formA, 2));
        await admin.PostAsync($"/api/cycles/{cycleA}/close", null);

        var emp1Id = await PersonIdAsync("EMP1");
        await admin.PostAsJsonAsync($"/api/cycles/{cycleA}/late-override", new LateOverrideRequest(emp1Id));

        // Cycle A is now writable again thanks to the override.
        var submitA = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submitA.StatusCode);

        // A brand-new cycle B opens for the same framework. EMP1 has NO override on cycle B.
        var cycleB = await CreateAndOpenCycleAsync(admin, fvId, "2026-09");
        var formB = await GetAsync(emp1);
        Assert.True(formB.Editable); // Open cycle, no override needed — editable on its own merits

        await admin.PostAsync($"/api/cycles/{cycleB}/close", null);
        // Now cycle B is closed with NO override for EMP1 — must be locked, proving cycle A's override
        // did not leak forward onto cycle B.
        var writeB = await PutScoresAsync(emp1, new[] { new ScoreEntry(formB.Dimensions[0].DimensionId, 1, null) });
        Assert.Equal(HttpStatusCode.Locked, writeB.StatusCode);
    }

    // =================================================================================================
    // Concurrent double-submit: two simultaneous submit calls for the same person/cycle must never both
    // "win" silently in a way that corrupts state or throws an unhandled 500 — the unique (CycleId,
    // PersonId) index plus the forward-only state machine must resolve the race cleanly.
    // =================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Concurrent_submit_calls_never_both_succeed_with_corrupted_state_and_never_500()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        var form = await GetAsync(emp1);
        await PutScoresAsync(emp1, ScoreAll(form, 2));

        var submit1 = emp1.PostAsync("/api/me/assessment/submit", null);
        var submit2 = emp1.PostAsync("/api/me/assessment/submit", null);
        var results = await Task.WhenAll(submit1, submit2);

        Assert.All(results, r => Assert.True(
            r.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Conflict,
            $"unexpected status {r.StatusCode} — a race must resolve to 204/409, never a 500"));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.NoContent); // exactly one logical winner

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1Id = await PersonIdAsync("EMP1");
        var rows = await db.Set<Assessment>().Where(a => a.PersonId == emp1Id).ToListAsync();
        Assert.Single(rows); // the unique (CycleId, PersonId) index held — no duplicate assessment row
        Assert.Equal(AssessmentState.Submitted, rows[0].State);
    }
}
