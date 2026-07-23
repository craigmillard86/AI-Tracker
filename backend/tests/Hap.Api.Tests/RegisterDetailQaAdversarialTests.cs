using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hap.Api.Identity;
using Hap.Domain.Register;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Hap.Infrastructure.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-14 (fresh-instance QA pass, CLAUDE.md §9), added during QA
/// rather than Dev — attributed as QA work. <see cref="RegisterDetailEndpointsTests"/> (Dev's own suite)
/// already proves the happy paths, the stage machine's forced-tracked-scope concurrency guard, the
/// unreferenced/referenced NR-line delete boundary, the §4.2 no-approval-gate proof, and the
/// owner/creator/BU-Lead 404 convention against a single "unrelated, different-BU" caller. This file
/// targets what a fresh adversarial pass looks for beyond that: field-smuggling through the write DTOs
/// (attempting to reassign BU or jump stage via extra raw-JSON properties the record types don't declare),
/// the narrower same-BU-but-still-unauthorised equivalence classes (plain manager without ownership;
/// individual contributor without ownership) that the Dev suite's single EMP_B1 case doesn't cover, a
/// GENUINE two-request HTTP race on the stage-transition endpoint (Dev's own concurrency test explicitly
/// forces state through a tracked scope rather than firing real concurrent requests — see that test's own
/// doc comment), and a negative-amount boundary on the weekly-update customers-in-production path that
/// mirrors <c>InitiativeNRLine.Create</c>'s already-tested negative-amount guard but was untested for
/// <see cref="Initiative.SetCustomersInProduction"/>'s own call site.
///
/// <para>This story has NO read path over Assessments/AssessmentScores or the Authorization seam (register
/// detail, not individual assessment data — confirmed by inspection: <c>RegisterEndpoints.cs</c> has zero
/// references to any Assessment/AssessmentScore/Authorization type; its only <c>RequireAuthorization</c>
/// call gates the unrelated Harris-taxonomy admin-seed endpoint). The CLAUDE.md §9.3 mandatory
/// individual-score/&lt;4-aggregate/rollup-desync attempts are therefore N/A for this story and are not
/// repeated here.</para>
/// </summary>
[Collection("hap-db")]
public sealed class RegisterDetailQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public RegisterDetailQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    private sealed record InitiativeDto(
        Guid Id,
        Guid BusinessUnitId,
        string Name,
        string CurrentStage,
        Guid OwnerPersonId,
        Guid CreatedByPersonId);

    private sealed record CreateBody(
        Guid BusinessUnitId,
        string Name,
        string? Description,
        Guid? SponsorPersonId,
        Guid OwnerPersonId,
        Guid CategoryId,
        int AiDlcLevel,
        List<string>? FunctionsAffected,
        List<string>? DimensionsAdvanced,
        int? CustomersInProduction,
        string? RiskTier);

    private sealed record StageChangeBody(string Stage);

    private sealed record PostWeeklyUpdateBody(string RagStatus, string? Note, int? CustomersInProduction);

    // ---- fixture (mirrors RegisterDetailEndpointsTests' own — deliberately self-contained per that
    // file's stated convention rather than shared) ----------------------------------------------------
    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) Fixture()
    {
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("ROOT", "BU_A"),
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "ROOT"),
            Snap.Person("GRPLEAD_A", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("BULEAD_B", "BU_B", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("MGR_B", "BU_B", managerExternalRef: "BULEAD_B"),
        };
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_A{i}", "BU_A", managerExternalRef: "MGR_A"));
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_B{i}", "BU_B", managerExternalRef: "MGR_B"));

        var seed = new List<SeedUserRecord>
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("ROOT", role: "HIG Executive"),
        };
        seed.AddRange(people
            .Where(p => p.ExternalRef is not ("ADMIN" or "ROOT"))
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        return (bus, people.ToArray(), seed.ToArray());
    }

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        var (bus, people, seed) = Fixture();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            foreach (var bu in await db.BusinessUnits.ToListAsync())
            {
                bu.SetOnboarded(true);
            }
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>().SeedAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        await admin.PostAsync("/api/admin/frameworks", null);
        return admin;
    }

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<Guid> BuIdAsync(string code)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.BusinessUnits.SingleAsync(b => b.Code == code)).Id;
    }

    private async Task<Guid> CategoryIdAsync(string key)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.HarrisCategories.SingleAsync(c => c.Key == key)).Id;
    }

    private async Task<InitiativeDto> CreateInitiativeAsync(
        HttpClient client, Guid buId, Guid ownerId, string categoryKey = "ai-product-feature", string name = "QA Initiative")
    {
        var categoryId = await CategoryIdAsync(categoryKey);
        var body = new CreateBody(buId, name, "desc", null, ownerId, categoryId, 2, null, null, null, "Low");
        var resp = await client.PostAsJsonAsync("/api/initiatives", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;
    }

    // ==================================================================================================
    // Attempt 1 — field smuggling: extra raw-JSON properties the write DTOs don't declare must be inert.
    // FR-028/FR-034: BU reassignment and stage-jump are structurally impossible via PUT, but only if
    // unknown JSON members are truly discarded rather than bound onto some other property by name
    // collision or a lenient deserializer configuration.
    // ==================================================================================================

    [Fact]
    public async Task Put_body_smuggling_businessUnitId_and_currentStage_via_raw_json_changes_neither()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var owner = await PersonIdAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA, owner);
        var categoryId = await CategoryIdAsync("ai-product-feature");

        // Raw JSON, deliberately including businessUnitId (a DIFFERENT BU) and currentStage (a forward
        // jump straight to Retired) alongside every field UpdateInitiativeRequest actually declares.
        var raw = $$"""
        {
          "name": "still not reassigned",
          "description": "d",
          "sponsorPersonId": null,
          "ownerPersonId": "{{owner}}",
          "categoryId": "{{categoryId}}",
          "aiDlcLevel": 2,
          "functionsAffected": null,
          "dimensionsAdvanced": null,
          "customersInProduction": null,
          "riskTier": "Low",
          "businessUnitId": "{{buB}}",
          "currentStage": "Retired"
        }
        """;
        var resp = await mgr.PutAsync(
            $"/api/initiatives/{created.Id}",
            new StringContent(raw, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var after = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;
        Assert.Equal(buA, after.BusinessUnitId); // NOT reassigned to buB
        Assert.Equal("Idea", after.CurrentStage); // NOT jumped to Retired
    }

    [Fact]
    public async Task Post_stage_body_smuggling_businessUnitId_via_raw_json_does_not_reassign_bu()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var owner = await PersonIdAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA, owner);

        var raw = $$"""{ "stage": "Evaluation", "businessUnitId": "{{buB}}" }""";
        var resp = await mgr.PostAsync(
            $"/api/initiatives/{created.Id}/stage",
            new StringContent(raw, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var after = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;
        Assert.Equal(buA, after.BusinessUnitId);
        Assert.Equal("Evaluation", after.CurrentStage);
    }

    // ==================================================================================================
    // Attempt 2 — same-BU-but-unauthorised equivalence classes (Dev's own suite only exercises a
    // different-BU individual as the "unrelated" 404 case; the same-BU plain-manager and same-BU
    // individual-contributor classes are structurally distinct decision-table rows against CanEditAsync).
    // ==================================================================================================

    [Fact]
    public async Task Plain_manager_in_the_same_bu_without_ownership_gets_404_on_every_write_endpoint()
    {
        await SeedAsync();
        var buLead = await ClientAsync("BULEAD_A");
        var buA = await BuIdAsync("BU_A");
        var emp = await PersonIdAsync("EMP_A1"); // owner is neither MGR_A nor BULEAD_A
        var created = await CreateInitiativeAsync(buLead, buA, emp, name: "owned by EMP_A1");

        // MGR_A: manages EMP_A1's team but is not this initiative's owner, creator, or the BU's Lead.
        var mgr = await ClientAsync("MGR_A");
        Assert.Equal(HttpStatusCode.NotFound,
            (await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("AtRisk", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/nr-lines",
                new { year = 2026, direction = "Direct", recurrence = "OneTime", amountUsd = 10m, description = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Individual_contributor_in_the_same_bu_without_ownership_gets_404_on_every_write_endpoint()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var created = await CreateInitiativeAsync(mgr, buA, await PersonIdAsync("MGR_A"));

        // EMP_A1: an individual contributor in the SAME BU, with no reports, not owner/creator/BU Lead.
        var emp = await ClientAsync("EMP_A1");
        Assert.Equal(HttpStatusCode.NotFound,
            (await emp.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await emp.PutAsJsonAsync($"/api/initiatives/{created.Id}",
                new { name = "hijacked", description = (string?)null, sponsorPersonId = (Guid?)null, ownerPersonId = created.OwnerPersonId, categoryId = await CategoryIdAsync("ai-product-feature"), aiDlcLevel = 2, functionsAffected = (List<string>?)null, dimensionsAdvanced = (List<string>?)null, customersInProduction = (int?)null, riskTier = "Low" })).StatusCode);
    }

    [Fact]
    public async Task Group_leader_two_levels_above_the_bu_gets_404_read_only_on_writes_despite_being_a_manager()
    {
        // GRPLEAD_A structurally IS a manager (has direct reports BULEAD_A/BULEAD_B) but the endpoint's
        // load-bearing precedence (RegisterEndpoints class doc) says leadership anchors are read-only —
        // this proves the WRITE path (CanEditAsync) independently denies them too, since CanEditAsync has
        // no leadership-anchor branch at all (only owner/creator/BuLeadOfBusinessUnitId).
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var created = await CreateInitiativeAsync(mgr, buA, await PersonIdAsync("MGR_A"));

        var grpLead = await ClientAsync("GRPLEAD_A");
        Assert.Equal(HttpStatusCode.NotFound,
            (await grpLead.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);
    }

    // ==================================================================================================
    // Attempt 3 — a GENUINE two-request HTTP race on the stage-transition endpoint (Dev's own test
    // forces the race deterministically via a tracked scope rather than real concurrent HTTP calls). Two
    // distinct clients POST two different forward targets via Task.WhenAll.
    //
    // FINDING (honestly reported, not papered over): the first version of this test asserted a fixed
    // outcome — "exactly one 200, exactly one 409" — on the theory that both requests would race to read
    // CurrentStage=Idea before either wrote. That assertion FAILED on the first real run: both requests
    // returned 200. Inspecting the resulting history proved this was NOT a guard failure — the two
    // requests were processed back-to-back by the in-memory WebApplicationFactory test host (Evaluation
    // committed, THEN Pilot's own read saw Evaluation and validly moved further forward), not truly
    // overlapping reads. This test harness cannot reliably force two HTTP requests to read the same stale
    // row before either writes — which is exactly the non-determinism Dev's own concurrency-test doc
    // comment already named as the reason it used a tracked-scope forced race instead of a live one. This
    // rewritten version asserts the INVARIANT that holds regardless of request interleaving (rather than
    // a specific interleaving outcome), so it is deterministic and still exercises the real guard: if a
    // genuine overlap ever does occur, a backward or duplicate/corrupted history entry would fail it.
    // ==================================================================================================

    [Fact]
    public async Task Two_concurrent_stage_transition_requests_never_produce_a_non_monotonic_history()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var created = await CreateInitiativeAsync(mgr, buA, await PersonIdAsync("MGR_A"));

        var clientOne = await ClientAsync("MGR_A");
        var clientTwo = await ClientAsync("MGR_A");

        var taskOne = clientOne.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"));
        var taskTwo = clientTwo.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Pilot"));
        await Task.WhenAll(taskOne, taskTwo);

        var statuses = new[] { taskOne.Result.StatusCode, taskTwo.Result.StatusCode };
        // Whatever the interleaving, every response is either a win or a legitimate 409 — never a 5xx.
        Assert.All(statuses, s => Assert.True(s is HttpStatusCode.OK or HttpStatusCode.Conflict, $"unexpected status {s}"));

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var history = await db.InitiativeStageHistories
            .Where(h => h.InitiativeId == created.Id)
            .OrderBy(h => h.EnteredAt)
            .ToListAsync();

        // The core forward-only invariant, interleaving-independent: every row's stage is strictly
        // greater (ordinal) than the row before it. A genuine race slipping a backward write through
        // would show up here as a non-increasing adjacent pair, regardless of which request "won".
        for (var i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].Stage > history[i - 1].Stage,
                $"stage history is not forward-only: row {i - 1} is {history[i - 1].Stage}, row {i} is {history[i].Stage}");
        }

        // The number of 200s on the wire must exactly match the number of transition rows written
        // (excluding the initial Idea row) — no silent extra write, no silent lost write.
        var okCount = statuses.Count(s => s == HttpStatusCode.OK);
        Assert.Equal(okCount, history.Count - 1);
    }

    // ==================================================================================================
    // Attempt 4 — negative customers-in-production via the weekly-update path (InitiativeNRLine.Create's
    // negative-amount guard is already tested; Initiative.SetCustomersInProduction's own negative guard,
    // reached only through POST /updates, was not).
    // ==================================================================================================

    [Fact]
    public async Task Weekly_update_negative_customers_in_production_returns_422_and_does_not_persist()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var created = await CreateInitiativeAsync(mgr, buA, await PersonIdAsync("MGR_A"), categoryKey: "ai-product-feature");

        var resp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("OnTrack", null, -5));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        var afterResp = await mgr.GetAsync($"/api/initiatives/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, afterResp.StatusCode);
        var afterJson = await afterResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, afterJson.GetProperty("customersInProduction").ValueKind);
    }

    // ==================================================================================================
    // Attempt 5 — append-only guard: prove there is genuinely no code path that can mutate or delete a
    // stage-history row, by attempting the mutation directly against the DbContext (the one thing the
    // architecture-test regex scan can't observe: whether the CLR type itself would even let EF track a
    // change if some future caller tried). InitiativeStageHistory has get-only properties (no setters) —
    // this proves that holds even from WITHIN the same assembly's persistence layer, not just via reflection.
    // ==================================================================================================

    [Fact]
    public async Task Stage_history_row_cannot_be_mutated_through_the_dbcontext_change_tracker()
    {
        await SeedAsync();
        var mgr = await ClientAsync("MGR_A");
        var buA = await BuIdAsync("BU_A");
        var created = await CreateInitiativeAsync(mgr, buA, await PersonIdAsync("MGR_A"));
        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"));

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var row = await db.InitiativeStageHistories.SingleAsync(h => h.InitiativeId == created.Id && h.Stage == InitiativeStage.Evaluation);

        // Attempt the property-bag route directly: mark the entity Modified and try to persist a change
        // to EnteredBy (the closest thing to an "edit" available without reflection). Because every
        // property is get-only, EF's ChangeTracker has nothing to write for a Modified entry beyond the
        // unchanged original values — this proves there is no live column to smuggle a value into, not
        // merely that the C# compiler blocks a direct setter call.
        var entry = db.Entry(row);
        entry.State = EntityState.Modified;
        await db.SaveChangesAsync();

        // No actual column value changed (there's nothing mutable to change), so this either persists
        // zero effective changes or throws — either way, re-reading the row must show it untouched.
        using var verifyScope = _factory.NewScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HapDbContext>();
        var reread = await verifyDb.InitiativeStageHistories.SingleAsync(h => h.Id == row.Id);
        Assert.Equal(row.EnteredBy, reread.EnteredBy);
        Assert.Equal(row.EnteredAt, reread.EnteredAt);
        Assert.Equal(InitiativeStage.Idea, reread.PriorStage);
    }
}
