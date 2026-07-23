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
/// HAP-13 register core: create/edit authority (FR-034), search + facets (FR-035), and the Harris
/// taxonomy seed (FR-027/FR-064). Not <c>Category=PrivacyReporting</c> — the register holds initiative
/// data, not individual assessment data. The fixture reuses the maturity dashboards' engineered
/// hierarchy so the seven roles resolve exactly (Exec/Portfolio/Group/BU Lead/Manager/Individual).
/// </summary>
[Collection("hap-db")]
public sealed class RegisterEndpointsTests
{
    private readonly HapApiFactory _factory;

    public RegisterEndpointsTests(HapApiFactory factory) => _factory = factory;

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
        string? RiskTier);

    private sealed record HarrisCategoryDto(Guid Id, string Key, string Name, bool GroupReported, bool CustomerDeployed);

    private sealed record BusinessUnitDto(Guid Id, string Code, string Name);

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

    /// <summary>Full seed: directory sync + onboard, framework (for dimension validation), Harris
    /// taxonomy. Returns an admin client.</summary>
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
            // Harris taxonomy seed (idempotent) via the DI-registered seeder.
            await scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>().SeedAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        // Framework seed — provides the active version whose dimension keys initiatives validate against.
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

    private async Task<CreateBody> ValidBodyAsync(
        Guid buId,
        Guid? ownerId = null,
        string name = "Claims Triage Copilot",
        int aiDlcLevel = 2,
        string? categoryKey = null,
        string riskTier = "Low",
        List<string>? dimensions = null,
        int? customers = null)
    {
        var owner = ownerId ?? await PersonIdAsync("MGR_A");
        var categoryId = await CategoryIdAsync(categoryKey ?? "ai-product-feature");
        return new CreateBody(
            buId, name, "desc", null, owner, categoryId, aiDlcLevel,
            new List<string> { "Claims" }, dimensions, customers, riskTier);
    }

    // ==================================================================================================
    // POST role matrix (FR-034)
    // ==================================================================================================

    [Fact]
    public async Task Manager_and_bu_lead_can_create_in_their_own_bu()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");

        var mgr = await ClientAsync("MGR_A");
        var mgrResp = await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA));
        Assert.Equal(HttpStatusCode.Created, mgrResp.StatusCode);

        var lead = await ClientAsync("BULEAD_A");
        var leadResp = await lead.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "Care-Plan Summariser"));
        Assert.Equal(HttpStatusCode.Created, leadResp.StatusCode);
    }

    [Fact]
    public async Task Bu_lead_and_manager_cannot_create_in_another_bu()
    {
        await SeedAsync();
        var buB = await BuIdAsync("BU_B");

        // BU Lead A → BU_B is cross-BU → 403.
        var leadA = await ClientAsync("BULEAD_A");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadA.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buB))).StatusCode);

        // Manager A → BU_B (not their home BU) → 403.
        var mgrA = await ClientAsync("MGR_A");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await mgrA.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buB))).StatusCode);
    }

    [Fact]
    public async Task Above_bu_roles_and_plain_individual_cannot_create()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");

        foreach (var who in new[] { "GRPLEAD_A", "PFLEAD", "ROOT", "EMP_A1" })
        {
            var client = await ClientAsync(who);
            var resp = await client.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
    }

    // ---- QA (HAP-13 adversarial): the dev role-matrix above exercises Manager (allow), BU Lead
    // (allow), Group Leader / Portfolio Leader / HIG Executive / Individual (deny) — six of the seven
    // seeded roles. Platform Admin is untested: it holds an OrgRole.PlatformAdmin grant (not
    // HigExecutive), has no reports and no BU Lead/leadership anchor in the fixture, so it must fall
    // through ResolveWritableBusinessUnitAsync's final "Individual" branch → deny. Completing the matrix.
    [Fact]
    public async Task QA_platform_admin_cannot_create()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");

        var admin = await ClientAsync("ADMIN");
        var resp = await admin.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_out_of_range_ai_dlc_level_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var resp = await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, aiDlcLevel: 4));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // QA: lower boundary-value pair for the AC's "AI-DLC level (1–3 validated)" — the dev suite only
    // covers the upper out-of-range case (4). 0 must also 422 (Guard: `aiDlcLevel is < 1 or > 3`).
    [Fact]
    public async Task QA_create_rejects_ai_dlc_level_zero_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var resp = await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, aiDlcLevel: 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Create_sets_stage_idea_rag_ontrack_and_maps_harris_stage()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var resp = await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA));
        var dto = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;

        Assert.Equal(nameof(InitiativeStage.Idea), dto.CurrentStage);
        Assert.Equal(nameof(RagStatus.OnTrack), dto.RagStatus);
        Assert.Equal(nameof(HarrisStage.Ideation), dto.HarrisStage); // Idea → Ideation (FR-064)
        Assert.Equal(dto.RegisteredAt, dto.LastUpdateAt);
    }

    // QA: a phantom category ID (well-formed GUID, no matching row) is untested by the dev suite —
    // covers the FK-integrity edge that keeps a bogus categoryId from ever reaching Initiative.Create.
    [Fact]
    public async Task QA_create_rejects_unknown_category_id_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var body = await ValidBodyAsync(buA);
        var withBogusCategory = body with { CategoryId = Guid.NewGuid() };
        var resp = await mgr.PostAsJsonAsync("/api/initiatives", withBogusCategory);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unknown_dimension_key_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var body = await ValidBodyAsync(buA, dimensions: new List<string> { "not-a-real-dimension" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await mgr.PostAsJsonAsync("/api/initiatives", body)).StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unrecognised_risk_tier_with_422()
    {
        // A non-null, unparseable risk tier must 422 rather than silently coerce to Low (FR-030).
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var body = await ValidBodyAsync(buA, riskTier: "Medium");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await mgr.PostAsJsonAsync("/api/initiatives", body)).StatusCode);
    }

    // QA finding, FIXED: TryParseRiskTier used bare Enum.TryParse, which — unlike a named unrecognised
    // value such as "Medium" above — accepted ANY numeric string and produced an UNDEFINED RiskTier
    // (Enum.TryParse succeeds for any value convertible to the underlying integral type regardless of
    // whether that value is declared; RiskTier is Low=0/Med=1/High=2, so "99" had no defined member). The
    // create used to succeed and the undefined value round-tripped intact through the string-converted DB
    // column. Now closed by an Enum.IsDefined check alongside the TryParse — this asserts the fix: 422,
    // nothing persisted.
    [Fact]
    public async Task Create_rejects_numeric_risk_tier_with_422_and_persists_nothing()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var body = await ValidBodyAsync(buA, riskTier: "99", name: "should-not-persist");
        var resp = await mgr.PostAsJsonAsync("/api/initiatives", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False(await db.Initiatives.AnyAsync(i => i.Name == "should-not-persist"));
    }

    // ==================================================================================================
    // Facets + search (FR-035)
    // ==================================================================================================

    [Fact]
    public async Task Facet_business_unit_filters()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var mgrA = await ClientAsync("MGR_A");
        var mgrB = await ClientAsync("MGR_B");

        await mgrA.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "A-one"));
        await mgrB.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buB, ownerId: await PersonIdAsync("MGR_B"), name: "B-one"));

        var filtered = await ListAsync(mgrA, $"?bu={buA}");
        Assert.All(filtered, i => Assert.Equal(buA, i.BusinessUnitId));
        Assert.Contains(filtered, i => i.Name == "A-one");
        Assert.DoesNotContain(filtered, i => i.Name == "B-one");
    }

    [Fact]
    public async Task Facet_category_filters()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var feature = await CategoryIdAsync("ai-product-feature");
        var other = await CategoryIdAsync("other-internal");

        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "feat", categoryKey: "ai-product-feature"));
        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "oth", categoryKey: "other-internal"));

        var filtered = await ListAsync(mgr, $"?category={feature}");
        Assert.All(filtered, i => Assert.Equal(feature, i.CategoryId));
        Assert.DoesNotContain(filtered, i => i.CategoryId == other);
    }

    [Fact]
    public async Task Facet_stage_filters()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var keep = (await (await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "stays-idea")))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;
        var moved = (await (await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "went-pilot")))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        // No stage-change endpoint in this story (HAP-14) — force one stage directly to prove the facet filters.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE initiatives SET \"CurrentStage\" = 'Pilot' WHERE \"Id\" = {0}", moved.Id);
        }

        var ideas = await ListAsync(mgr, "?stage=Idea");
        Assert.Contains(ideas, i => i.Id == keep.Id);
        Assert.DoesNotContain(ideas, i => i.Id == moved.Id);

        var pilots = await ListAsync(mgr, "?stage=Pilot");
        Assert.Contains(pilots, i => i.Id == moved.Id);
        Assert.DoesNotContain(pilots, i => i.Id == keep.Id);
    }

    [Fact]
    public async Task Facet_risk_tier_filters()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "low-risk", riskTier: "Low"));
        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "high-risk", riskTier: "High"));

        var high = await ListAsync(mgr, "?riskTier=High");
        Assert.All(high, i => Assert.Equal("High", i.RiskTier));
        Assert.Contains(high, i => i.Name == "high-risk");
        Assert.DoesNotContain(high, i => i.Name == "low-risk");
    }

    [Fact]
    public async Task Facet_ai_dlc_level_filters()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "lvl1", aiDlcLevel: 1));
        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "lvl3", aiDlcLevel: 3));

        var lvl3 = await ListAsync(mgr, "?aiDlcLevel=3");
        Assert.All(lvl3, i => Assert.Equal(3, i.AiDlcLevel));
        Assert.Contains(lvl3, i => i.Name == "lvl3");
        Assert.DoesNotContain(lvl3, i => i.Name == "lvl1");
    }

    [Fact]
    public async Task Facet_dimension_filters_via_array_contains()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var keys = await AllDimensionKeysAsync();
        var dimX = keys[0];
        var dimY = keys[1];

        await mgr.PostAsJsonAsync("/api/initiatives",
            await ValidBodyAsync(buA, name: "has-x", dimensions: new List<string> { dimX }));
        await mgr.PostAsJsonAsync("/api/initiatives",
            await ValidBodyAsync(buA, name: "has-y", dimensions: new List<string> { dimY }));

        var withX = await ListAsync(mgr, $"?dimension={Uri.EscapeDataString(dimX)}");
        Assert.All(withX, i => Assert.Contains(dimX, i.DimensionsAdvanced));
        Assert.Contains(withX, i => i.Name == "has-x");
        Assert.DoesNotContain(withX, i => i.Name == "has-y");
    }

    [Fact]
    public async Task Full_text_search_matches_name_case_insensitively()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "Claims Triage Copilot"));
        await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA, name: "Referral Intake Agent"));

        var hits = await ListAsync(mgr, "?search=triage");
        Assert.Contains(hits, i => i.Name == "Claims Triage Copilot");
        Assert.DoesNotContain(hits, i => i.Name == "Referral Intake Agent");
    }

    // ==================================================================================================
    // PUT permission (FR-034)
    // ==================================================================================================

    [Fact]
    public async Task Put_allows_owner_creator_and_bu_lead_but_denies_unrelated()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var ownerId = await PersonIdAsync("EMP_A1"); // owner distinct from creator (MGR_A)

        var created = (await (await mgr.PostAsJsonAsync("/api/initiatives",
                await ValidBodyAsync(buA, ownerId: ownerId)))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        var categoryId = await CategoryIdAsync("ai-product-feature");
        UpdateBody Edit(string name) =>
            new(name, "d", null, ownerId, categoryId, 2, null, null, null, "Low");

        // Creator (MGR_A) → allowed.
        Assert.Equal(HttpStatusCode.OK,
            (await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", Edit("by-creator"))).StatusCode);

        // Owner (EMP_A1, not the creator) → allowed.
        var owner = await ClientAsync("EMP_A1");
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PutAsJsonAsync($"/api/initiatives/{created.Id}", Edit("by-owner"))).StatusCode);

        // BU Lead of BU_A → allowed.
        var lead = await ClientAsync("BULEAD_A");
        Assert.Equal(HttpStatusCode.OK,
            (await lead.PutAsJsonAsync($"/api/initiatives/{created.Id}", Edit("by-bulead"))).StatusCode);

        // Unrelated person (individual in another BU) → 404 (existence-leak convention).
        var unrelated = await ClientAsync("EMP_B1");
        Assert.Equal(HttpStatusCode.NotFound,
            (await unrelated.PutAsJsonAsync($"/api/initiatives/{created.Id}", Edit("nope"))).StatusCode);
    }

    [Fact]
    public async Task Put_denies_bu_lead_of_a_different_bu()
    {
        // Exercises CanEditAsync's cross-BU branch specifically: BULEAD_B IS a BU Lead (has a real
        // BuLeadOfBusinessUnitId anchor), just not of the initiative's BU — distinct from the
        // no-anchor-at-all "unrelated person" case above.
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var created = (await (await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA)))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        var categoryId = await CategoryIdAsync("ai-product-feature");
        var ownerId = await PersonIdAsync("MGR_A");
        var edit = new UpdateBody("nope", "d", null, ownerId, categoryId, 2, null, null, null, "Low");

        var leadB = await ClientAsync("BULEAD_B");
        Assert.Equal(HttpStatusCode.NotFound,
            (await leadB.PutAsJsonAsync($"/api/initiatives/{created.Id}", edit)).StatusCode);
    }

    [Fact]
    public async Task Put_cannot_change_business_unit_or_stage()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");
        var created = (await (await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA)))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        var categoryId = await CategoryIdAsync("ai-product-feature");
        var edited = (await (await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}",
                new UpdateBody("renamed", "d", null, created.OwnerPersonId, categoryId, 3, null, null, 5, "High")))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        Assert.Equal("renamed", edited.Name);
        Assert.Equal(3, edited.AiDlcLevel);
        Assert.Equal(buA, edited.BusinessUnitId);                 // unchanged — no BU reassignment path
        Assert.Equal(nameof(InitiativeStage.Idea), edited.CurrentStage); // unchanged — no stage change here
    }

    // QA: the dev test above proves BU/stage stay put using the typed UpdateBody, which has no
    // businessUnitId/currentStage properties at all — but that alone doesn't rule out a raw-JSON
    // smuggling attempt against a caller who bypasses the typed client and posts extra fields directly.
    // System.Text.Json's default UnmappedMemberHandling is Skip (no [JsonUnmappedMemberHandling] set
    // anywhere in the API), so extra properties are silently ignored rather than rejected — confirming
    // by direct attempt, not just by the contract shape, that a forged businessUnitId/currentStage in
    // the wire body cannot reach Initiative.Edit (which doesn't accept them as parameters either way).
    [Fact]
    public async Task QA_put_ignores_forged_business_unit_and_stage_fields_in_raw_json()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var mgr = await ClientAsync("MGR_A");
        var created = (await (await mgr.PostAsJsonAsync("/api/initiatives", await ValidBodyAsync(buA)))
            .Content.ReadFromJsonAsync<InitiativeDto>())!;

        var categoryId = await CategoryIdAsync("ai-product-feature");
        var raw = new
        {
            name = "forged-attempt",
            description = "d",
            sponsorPersonId = (Guid?)null,
            ownerPersonId = created.OwnerPersonId,
            categoryId,
            aiDlcLevel = 2,
            functionsAffected = (List<string>?)null,
            dimensionsAdvanced = (List<string>?)null,
            customersInProduction = (int?)null,
            riskTier = "Low",
            businessUnitId = buB,                       // forged — attempt cross-BU reassignment
            currentStage = nameof(InitiativeStage.Production), // forged — attempt to skip the stage machine
        };

        var resp = await mgr.PutAsJsonAsync($"/api/initiatives/{created.Id}", raw);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var edited = (await resp.Content.ReadFromJsonAsync<InitiativeDto>())!;

        Assert.Equal(buA, edited.BusinessUnitId);                        // forged field had no effect
        Assert.Equal(nameof(InitiativeStage.Idea), edited.CurrentStage); // forged field had no effect
    }

    // ==================================================================================================
    // Harris taxonomy seed (FR-027)
    // ==================================================================================================

    [Fact]
    public async Task Harris_taxonomy_seeds_five_categories_with_correct_flags()
    {
        await SeedAsync();
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        var categories = await db.HarrisCategories.ToListAsync();
        Assert.Equal(5, categories.Count);

        var other = categories.Single(c => c.Key == "other-internal");
        Assert.False(other.GroupReported); // "Other" excluded from group-reported counts (FR-044)
        Assert.False(other.CustomerDeployed);

        // customer_deployed is true only for the three customer-facing categories.
        foreach (var key in new[] { "ai-product-feature", "pre-built-agent-customer", "custom-agent-customer" })
        {
            Assert.True(categories.Single(c => c.Key == key).CustomerDeployed, $"{key} should be customer_deployed");
        }
        foreach (var key in new[] { "digital-worker-internal", "other-internal" })
        {
            Assert.False(categories.Single(c => c.Key == key).CustomerDeployed, $"{key} should not be customer_deployed");
        }

        // All but "Other" are group-reported.
        Assert.All(categories.Where(c => c.Key != "other-internal"), c => Assert.True(c.GroupReported));
    }

    [Fact]
    public async Task Harris_stage_map_covers_all_six_stages_per_fr064()
    {
        await SeedAsync();
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        var map = await db.HarrisStageMaps.ToDictionaryAsync(s => s.InternalStage, s => s.HarrisStage);
        Assert.Equal(6, map.Count);
        Assert.Equal(HarrisStage.Ideation, map[InitiativeStage.Idea]);
        Assert.Equal(HarrisStage.Ideation, map[InitiativeStage.Evaluation]);
        Assert.Equal(HarrisStage.Development, map[InitiativeStage.Pilot]);
        Assert.Equal(HarrisStage.Production, map[InitiativeStage.Production]);
        Assert.Equal(HarrisStage.Production, map[InitiativeStage.Scaled]);
        Assert.Equal(HarrisStage.IdeasTriedButStopped, map[InitiativeStage.Retired]);
    }

    [Fact]
    public async Task Taxonomy_seed_is_idempotent()
    {
        await SeedAsync();
        using var scope = _factory.NewScope();
        var seeder = scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>();

        var second = await seeder.SeedAsync(); // already seeded in SeedAsync
        Assert.Equal(0, second.CategoriesCreated);
        Assert.Equal(0, second.StageMappingsCreated);
        Assert.Equal(5, second.CategoriesTotal);
        Assert.Equal(6, second.StageMappingsTotal);
    }

    // ==================================================================================================
    // Reference-data endpoints (filters + create/edit form)
    // ==================================================================================================

    [Fact]
    public async Task Harris_categories_endpoint_lists_all_five()
    {
        var admin = await SeedAsync();
        var resp = await admin.GetAsync("/api/harris-categories");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var categories = await resp.Content.ReadFromJsonAsync<List<HarrisCategoryDto>>();
        Assert.Equal(5, categories!.Count);
        Assert.Contains(categories, c => c.Key == "other-internal" && !c.GroupReported);
    }

    [Fact]
    public async Task Business_units_endpoint_lists_all_bus()
    {
        // The BU filter dropdown should show every BU, not just onboarded ones — the endpoint does not
        // (and should not) filter on onboarding.
        var admin = await SeedAsync();
        var resp = await admin.GetAsync("/api/business-units");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var units = await resp.Content.ReadFromJsonAsync<List<BusinessUnitDto>>();
        Assert.Equal(2, units!.Count);
        Assert.Contains(units, u => u.Code == "BU_A");
        Assert.Contains(units, u => u.Code == "BU_B");
    }

    // ---- helpers -------------------------------------------------------------------------------------
    private static async Task<List<InitiativeDto>> ListAsync(HttpClient client, string query) =>
        (await (await client.GetAsync($"/api/initiatives{query}")).Content.ReadFromJsonAsync<List<InitiativeDto>>())!;

    private async Task<List<string>> AllDimensionKeysAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.Dimensions.OrderBy(d => d.DisplayOrder).Select(d => d.Key).ToListAsync();
    }
}
