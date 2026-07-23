using System.Net;
using System.Net.Http.Json;
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
/// HAP-14 register detail: forward-only stage machine (FR-028), weekly RAG updates (FR-033), NR lines
/// (FR-029), governance fields (FR-030 — informational only, §4.2) and the FR-031 customers-in-production
/// category gating. Not <c>Category=PrivacyReporting</c> — the register holds initiative data, not
/// individual assessment data (matching <c>RegisterEndpointsTests</c>'s own convention). The fixture is
/// copied from <c>RegisterEndpointsTests</c> deliberately (each integration test file is self-contained
/// per that file's own convention) rather than shared, to keep each file's SeedAsync/ClientAsync free to
/// diverge without cross-file coupling.
/// </summary>
[Collection("hap-db")]
public sealed class RegisterDetailEndpointsTests
{
    private readonly HapApiFactory _factory;

    public RegisterDetailEndpointsTests(HapApiFactory factory) => _factory = factory;

    // ---- wire DTOs -----------------------------------------------------------------------------------
    private sealed record InitiativeDto(
        Guid Id,
        Guid BusinessUnitId,
        string Name,
        string? Description,
        Guid? SponsorPersonId,
        Guid OwnerPersonId,
        Guid CreatedByPersonId,
        DateTime RegisteredAt,
        Guid CategoryId,
        int AiDlcLevel,
        List<string> FunctionsAffected,
        List<string> DimensionsAdvanced,
        string CurrentStage,
        string? HarrisStage,
        string RagStatus,
        DateTime LastUpdateAt,
        int? CustomersInProduction,
        string RiskTier);

    private sealed record StageHistoryEntryDto(Guid Id, string Stage, string? PriorStage, DateTime EnteredAt, Guid EnteredBy);

    private sealed record NrLineDto(Guid Id, int Year, string Direction, string Recurrence, decimal AmountUsd, string? Description, bool Locked);

    private sealed record WeeklyUpdateDto(Guid Id, string RagStatus, string? Note, Guid CreatedBy, DateTime CreatedAt);

    private sealed record InitiativeDetailDto(
        Guid Id,
        Guid BusinessUnitId,
        string Name,
        string? Description,
        Guid? SponsorPersonId,
        Guid OwnerPersonId,
        Guid CreatedByPersonId,
        DateTime RegisteredAt,
        Guid CategoryId,
        int AiDlcLevel,
        List<string> FunctionsAffected,
        List<string> DimensionsAdvanced,
        string CurrentStage,
        string? HarrisStage,
        string RagStatus,
        DateTime LastUpdateAt,
        int? CustomersInProduction,
        string RiskTier,
        string DataSensitivity,
        List<string> RegulatoryRelevance,
        string? ApprovalStatus,
        string? Approver,
        string? OversightModel,
        string? GovernanceNotes,
        List<string> ModelsProviders,
        List<string> VendorsTools,
        bool UsesCogito,
        bool CanEdit,
        bool CategoryCustomerDeployed,
        List<StageHistoryEntryDto> StageHistory,
        List<NrLineDto> NrLines,
        List<WeeklyUpdateDto> Updates);

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

    private sealed record UpdateBody(
        string Name,
        string? Description,
        Guid? SponsorPersonId,
        Guid OwnerPersonId,
        Guid CategoryId,
        int AiDlcLevel,
        List<string>? FunctionsAffected,
        List<string>? DimensionsAdvanced,
        int? CustomersInProduction,
        string? RiskTier,
        string? DataSensitivity = null,
        List<string>? RegulatoryRelevance = null,
        string? ApprovalStatus = null,
        string? Approver = null,
        string? OversightModel = null,
        string? GovernanceNotes = null,
        List<string>? ModelsProviders = null,
        List<string>? VendorsTools = null,
        bool UsesCogito = false);

    private sealed record StageChangeBody(string Stage);

    private sealed record PostWeeklyUpdateBody(string RagStatus, string? Note, int? CustomersInProduction);

    private sealed record CreateNrLineBody(int Year, string Direction, string Recurrence, decimal AmountUsd, string? Description);

    // ---- fixture: ROOT[Exec] → PFLEAD[PortLdr] → GRPLEAD_A[GrpLdr] → BULEAD_A/_B → MGR_A/_B → EMPs --
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
        HttpClient client,
        Guid buId,
        string categoryKey = "ai-product-feature",
        string name = "Claims Triage Copilot",
        int? customers = null,
        Guid? ownerId = null)
    {
        var owner = ownerId ?? await PersonIdAsync("MGR_A");
        var categoryId = await CategoryIdAsync(categoryKey);
        var body = new CreateBody(
            buId, name, "desc", null, owner, categoryId, 2,
            new List<string> { "Claims" }, null, customers, "Low");
        var resp = await client.PostAsJsonAsync("/api/initiatives", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;
    }

    private static async Task<NrLineDto> CreateNrLineAsync(
        HttpClient client, Guid initiativeId, int year = 2026, string direction = "Direct",
        string recurrence = "OneTime", decimal amount = 50_000m, string? description = "NR line")
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/initiatives/{initiativeId}/nr-lines",
            new CreateNrLineBody(year, direction, recurrence, amount, description));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<NrLineDto>())!;
    }

    private static async Task<InitiativeDetailDto> GetDetailAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/initiatives/{id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<InitiativeDetailDto>())!;
    }

    // ==================================================================================================
    // Stage machine (FR-028)
    // ==================================================================================================

    [Fact]
    public async Task Stage_transition_walks_all_five_forward_steps_with_correctly_ordered_history()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        foreach (var stage in new[] { "Evaluation", "Pilot", "Production", "Scaled", "Retired" })
        {
            var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody(stage));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var dto = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;
            Assert.Equal(stage, dto.CurrentStage);
        }

        var detail = await GetDetailAsync(mgr, created.Id);
        var expectedStages = new[] { "Idea", "Evaluation", "Pilot", "Production", "Scaled", "Retired" };
        Assert.Equal(expectedStages, detail.StageHistory.Select(h => h.Stage));
        Assert.Null(detail.StageHistory[0].PriorStage); // initial Idea row — no "before"
        for (var i = 1; i < detail.StageHistory.Count; i++)
        {
            Assert.Equal(expectedStages[i - 1], detail.StageHistory[i].PriorStage);
        }
        // Oldest→newest: entered_at is monotonically non-decreasing.
        Assert.Equal(detail.StageHistory.OrderBy(h => h.EnteredAt).Select(h => h.Id), detail.StageHistory.Select(h => h.Id));
    }

    [Fact]
    public async Task Stage_transition_allows_a_multi_step_forward_jump()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Pilot"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal(new[] { "Idea", "Pilot" }, detail.StageHistory.Select(h => h.Stage));
        Assert.Equal("Idea", detail.StageHistory[1].PriorStage);
    }

    [Fact]
    public async Task Backward_transition_returns_409()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Pilot"));

        var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        // History unaffected by the rejected attempt.
        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal(new[] { "Idea", "Pilot" }, detail.StageHistory.Select(h => h.Stage));
    }

    [Fact]
    public async Task Same_stage_no_op_returns_409()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Idea"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Transition_from_retired_is_always_409_terminal()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Retired"));

        var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Retired"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Stage_write_by_a_reader_stale_from_a_committed_concurrent_transition_throws_concurrency_exception()
    {
        // Proves the xmin guard added to InitiativeConfiguration (panel finding A1): two "concurrent"
        // forward transitions each read CurrentStage before either writes. Genuine two-request HTTP
        // concurrency is inherently timing-dependent (whether both requests' reads land before either's
        // write is a race, not something a test can pin down), so — mirroring this file's own
        // NR_line_delete_returns_409_once_referenced_by_a_persisted_submission precedent of forcing state
        // through a tracked scope — this deterministically sequences the SAME race: a scope holds a
        // stale read (captured before the real transition below commits), and its later save must be
        // rejected by EF's optimistic-concurrency check rather than silently winning last-write-wins.
        // The endpoint's own catch (DbUpdateConcurrencyException) maps this exact exception to 409 —
        // that mapping is a trivial 3-line block, identical in shape to the already-reviewed
        // SeamAssessmentStore precedent (HAP-9), and is not re-proven via true concurrent HTTP calls here
        // for the same non-determinism reason.
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        using var staleScope = _factory.NewScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<HapDbContext>();
        // Captures xmin while CurrentStage is still Idea — the "concurrent reader"'s stale view.
        var staleTracked = await staleDb.Initiatives.SingleAsync(i => i.Id == created.Id);

        // The "winning" transition: committed via the real endpoint, bumping xmin server-side.
        var winnerResp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"));
        Assert.Equal(HttpStatusCode.OK, winnerResp.StatusCode);

        // The "losing" transition: forward from the stale reader's (now outdated) view of CurrentStage.
        staleTracked.AdvanceStage(InitiativeStage.Pilot);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        // The winner's transition is the only one on record — the stale loser's save never committed.
        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal(new[] { "Idea", "Evaluation" }, detail.StageHistory.Select(h => h.Stage));
    }

    [Fact]
    public async Task Unrecognised_stage_returns_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("NotAStage"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ==================================================================================================
    // Weekly updates (FR-033)
    // ==================================================================================================

    [Fact]
    public async Task Weekly_update_records_rag_and_note_and_refreshes_last_update_at()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("AtRisk", "slipping a week", null));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var updateDto = (await resp.Content.ReadFromJsonAsync<WeeklyUpdateDto>())!;
        Assert.Equal("AtRisk", updateDto.RagStatus);
        Assert.Equal("slipping a week", updateDto.Note);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal("AtRisk", detail.RagStatus);
        Assert.True(detail.LastUpdateAt > created.LastUpdateAt);
    }

    [Fact]
    public async Task Update_trail_is_newest_first_on_the_detail_endpoint()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("OnTrack", "first", null));
        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("AtRisk", "second", null));
        await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("OffTrack", "third", null));

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal(new[] { "third", "second", "first" }, detail.Updates.Select(u => u.Note));
    }

    [Fact]
    public async Task Weekly_update_customers_in_production_applies_only_for_customer_deployed_category()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var deployed = await CreateInitiativeAsync(mgr, buA, categoryKey: "ai-product-feature", name: "deployed-init");
        var internalInit = await CreateInitiativeAsync(mgr, buA, categoryKey: "digital-worker-internal", name: "internal-init");

        await mgr.PostAsJsonAsync($"/api/initiatives/{deployed.Id}/updates", new PostWeeklyUpdateBody("OnTrack", null, 42));
        await mgr.PostAsJsonAsync($"/api/initiatives/{internalInit.Id}/updates", new PostWeeklyUpdateBody("OnTrack", null, 42));

        Assert.Equal(42, (await GetDetailAsync(mgr, deployed.Id)).CustomersInProduction);
        Assert.Null((await GetDetailAsync(mgr, internalInit.Id)).CustomersInProduction);
    }

    [Fact]
    public async Task Unrecognised_rag_status_returns_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("Purple", null, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ==================================================================================================
    // NR lines (FR-029)
    // ==================================================================================================

    [Fact]
    public async Task NR_line_add_persists_correctly()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var line = await CreateNrLineAsync(mgr, created.Id, year: 2026, direction: "Direct", recurrence: "Recurring", amount: 50_000m, description: "Cost avoidance");

        Assert.Equal(2026, line.Year);
        Assert.Equal("Direct", line.Direction);
        Assert.Equal("Recurring", line.Recurrence);
        Assert.Equal(50_000m, line.AmountUsd);
        Assert.False(line.Locked);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Contains(detail.NrLines, l => l.Id == line.Id);
    }

    [Fact]
    public async Task NR_line_delete_succeeds_when_unreferenced()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        var line = await CreateNrLineAsync(mgr, created.Id);

        var resp = await mgr.DeleteAsync($"/api/initiatives/{created.Id}/nr-lines/{line.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.DoesNotContain(detail.NrLines, l => l.Id == line.Id);
    }

    [Fact]
    public async Task NR_line_delete_returns_409_once_referenced_by_a_persisted_submission()
    {
        // No HarrisSubmission table exists yet (HAP-16 scope) — simulate the locked state by calling the
        // domain method directly through a tracked scope, mirroring HAP-13's own Facet_stage_filters test
        // (which forced state via raw SQL for the same "the real writer doesn't exist yet" reason).
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        var line = await CreateNrLineAsync(mgr, created.Id);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var tracked = await db.InitiativeNRLines.SingleAsync(l => l.Id == line.Id);
            tracked.MarkReferencedBySubmission(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        var resp = await mgr.DeleteAsync($"/api/initiatives/{created.Id}/nr-lines/{line.Id}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Contains(detail.NrLines, l => l.Id == line.Id && l.Locked); // still present, now Locked
    }

    [Fact]
    public async Task NR_line_delete_of_a_line_belonging_to_a_different_initiative_returns_404()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var initiativeOne = await CreateInitiativeAsync(mgr, buA, name: "one");
        var initiativeTwo = await CreateInitiativeAsync(mgr, buA, name: "two");
        var lineOnOne = await CreateNrLineAsync(mgr, initiativeOne.Id);

        // Addressing initiativeTwo's route with a line that belongs to initiativeOne — no existence leak.
        var resp = await mgr.DeleteAsync($"/api/initiatives/{initiativeTwo.Id}/nr-lines/{lineOnOne.Id}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task NR_line_create_rejects_out_of_range_year_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/nr-lines", new CreateNrLineBody(1999, "Direct", "OneTime", 10m, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task NR_line_create_rejects_unrecognised_direction_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var resp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/nr-lines", new CreateNrLineBody(2026, "Sideways", "OneTime", 10m, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ==================================================================================================
    // Governance (FR-030 — informational only, §4.2)
    // ==================================================================================================

    [Fact]
    public async Task Governance_fields_persist_and_round_trip_via_put()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        var categoryId = await CategoryIdAsync("ai-product-feature");

        var edit = new UpdateBody(
            "renamed", "d", null, created.OwnerPersonId, categoryId, 2, null, null, null, "Low",
            DataSensitivity: "PII",
            RegulatoryRelevance: new List<string> { "GDPR", "HIPAA" },
            ApprovalStatus: "Pending",
            Approver: "Jane Exec",
            OversightModel: "Human-in-the-loop",
            GovernanceNotes: "Reviewed by legal",
            ModelsProviders: new List<string> { "Claude" },
            VendorsTools: new List<string> { "Azure OpenAI" },
            UsesCogito: true);

        var resp = await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", edit);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal("PII", detail.DataSensitivity);
        Assert.Equal(new List<string> { "GDPR", "HIPAA" }, detail.RegulatoryRelevance);
        Assert.Equal("Pending", detail.ApprovalStatus);
        Assert.Equal("Jane Exec", detail.Approver);
        Assert.Equal("Human-in-the-loop", detail.OversightModel);
        Assert.Equal("Reviewed by legal", detail.GovernanceNotes);
        Assert.Equal(new List<string> { "Claude" }, detail.ModelsProviders);
        Assert.Equal(new List<string> { "Azure OpenAI" }, detail.VendorsTools);
        Assert.True(detail.UsesCogito);
    }

    [Fact]
    public async Task Governance_defaults_to_none_and_empty_at_creation()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var detail = await GetDetailAsync(mgr, created.Id);
        Assert.Equal(nameof(DataSensitivity.None), detail.DataSensitivity);
        Assert.Empty(detail.RegulatoryRelevance);
        Assert.Null(detail.ApprovalStatus);
        Assert.Null(detail.Approver);
        Assert.Null(detail.OversightModel);
        Assert.Null(detail.GovernanceNotes);
        Assert.Empty(detail.ModelsProviders);
        Assert.Empty(detail.VendorsTools);
        Assert.False(detail.UsesCogito);
    }

    // AC30: the register is not an approval gate — no operation may ever be blocked by ApprovalStatus.
    [Fact]
    public async Task AC30_no_approval_status_value_ever_blocks_any_write_operation()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        var categoryId = await CategoryIdAsync("ai-product-feature");

        // Set an adversarial-looking approval status via Edit.
        var setRejected = new UpdateBody(
            "still-named", "d", null, created.OwnerPersonId, categoryId, 2, null, null, null, "Low",
            ApprovalStatus: "Rejected");
        Assert.Equal(HttpStatusCode.OK, (await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", setRejected)).StatusCode);

        // Every other write operation on this same initiative must still succeed 2xx.
        Assert.Equal(HttpStatusCode.OK,
            (await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await mgr.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("AtRisk", "still going", null))).StatusCode);

        var nrResp = await mgr.PostAsJsonAsync(
            $"/api/initiatives/{created.Id}/nr-lines", new CreateNrLineBody(2026, "Direct", "OneTime", 100m, null));
        Assert.Equal(HttpStatusCode.Created, nrResp.StatusCode);
        var nrLine = (await nrResp.Content.ReadFromJsonAsync<NrLineDto>())!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await mgr.DeleteAsync($"/api/initiatives/{created.Id}/nr-lines/{nrLine.Id}")).StatusCode);

        var editAgain = new UpdateBody(
            "edited-again", "d", null, created.OwnerPersonId, categoryId, 2, null, null, null, "Low",
            ApprovalStatus: "Rejected");
        Assert.Equal(HttpStatusCode.OK, (await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", editAgain)).StatusCode);
    }

    // ==================================================================================================
    // Customers-in-production category gating (FR-031) — POST/PUT retrofit
    // ==================================================================================================

    [Fact]
    public async Task Create_normalises_customers_in_production_to_null_for_non_customer_deployed_category()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var deployed = await CreateInitiativeAsync(mgr, buA, categoryKey: "ai-product-feature", name: "deployed", customers: 10);
        var internalInit = await CreateInitiativeAsync(mgr, buA, categoryKey: "digital-worker-internal", name: "internal", customers: 10);

        Assert.Equal(10, deployed.CustomersInProduction);
        Assert.Null(internalInit.CustomersInProduction);
    }

    [Fact]
    public async Task Put_normalises_customers_in_production_to_null_for_non_customer_deployed_category()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var internalCategoryId = await CategoryIdAsync("digital-worker-internal");
        var created = await CreateInitiativeAsync(mgr, buA, categoryKey: "ai-product-feature");

        var edit = new UpdateBody("renamed", "d", null, created.OwnerPersonId, internalCategoryId, 2, null, null, 99, "Low");
        var resp = await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", edit);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;

        Assert.Null(dto.CustomersInProduction);
    }

    [Fact]
    public async Task Put_persists_customers_in_production_for_a_customer_deployed_category()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var deployedCategoryId = await CategoryIdAsync("ai-product-feature");
        var created = await CreateInitiativeAsync(mgr, buA, categoryKey: "ai-product-feature");

        var edit = new UpdateBody("renamed", "d", null, created.OwnerPersonId, deployedCategoryId, 2, null, null, 17, "Low");
        var resp = await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", edit);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;

        Assert.Equal(17, dto.CustomersInProduction);
    }

    // ==================================================================================================
    // Role/permission (mirrors PUT's existing 404 existence-leak convention)
    // ==================================================================================================

    [Fact]
    public async Task Unrelated_person_gets_404_on_stage_updates_and_nr_line_endpoints()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);
        var line = await CreateNrLineAsync(mgr, created.Id);

        var unrelated = await ClientAsync("EMP_B1"); // different BU; not owner/creator/BU Lead of BU_A

        Assert.Equal(HttpStatusCode.NotFound,
            (await unrelated.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await unrelated.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("AtRisk", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await unrelated.PostAsJsonAsync($"/api/initiatives/{created.Id}/nr-lines", new CreateNrLineBody(2026, "Direct", "OneTime", 10m, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await unrelated.DeleteAsync($"/api/initiatives/{created.Id}/nr-lines/{line.Id}")).StatusCode);
    }

    [Fact]
    public async Task Bu_lead_of_the_owning_bu_can_use_stage_updates_and_nr_line_endpoints()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var lead = await ClientAsync("BULEAD_A");
        Assert.Equal(HttpStatusCode.OK,
            (await lead.PostAsJsonAsync($"/api/initiatives/{created.Id}/stage", new StageChangeBody("Evaluation"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await lead.PostAsJsonAsync($"/api/initiatives/{created.Id}/updates", new PostWeeklyUpdateBody("OnTrack", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await lead.PostAsJsonAsync($"/api/initiatives/{created.Id}/nr-lines", new CreateNrLineBody(2026, "Direct", "OneTime", 10m, null))).StatusCode);
    }

    // ==================================================================================================
    // GET detail — CanEdit / CategoryCustomerDeployed (HAP-14)
    // ==================================================================================================

    [Fact]
    public async Task Detail_endpoint_reports_can_edit_true_for_editor_and_false_for_non_editor()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = await CreateInitiativeAsync(mgr, buA);

        var editorDetail = await GetDetailAsync(mgr, created.Id);
        Assert.True(editorDetail.CanEdit);

        var unrelated = await ClientAsync("EMP_B1");
        var nonEditorResp = await unrelated.GetAsync($"/api/initiatives/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, nonEditorResp.StatusCode); // GET is readable by any role
        var nonEditorDetail = (await nonEditorResp.Content.ReadFromJsonAsync<InitiativeDetailDto>())!;
        Assert.False(nonEditorDetail.CanEdit);
    }

    [Fact]
    public async Task Detail_endpoint_reports_category_customer_deployed_matching_category()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var deployed = await CreateInitiativeAsync(mgr, buA, categoryKey: "ai-product-feature", name: "deployed");
        var internalInit = await CreateInitiativeAsync(mgr, buA, categoryKey: "digital-worker-internal", name: "internal");

        Assert.True((await GetDetailAsync(mgr, deployed.Id)).CategoryCustomerDeployed);
        Assert.False((await GetDetailAsync(mgr, internalInit.Id)).CategoryCustomerDeployed);
    }
}
