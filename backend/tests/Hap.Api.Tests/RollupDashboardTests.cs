using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Assessments;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-11 L3 guarantees for the aggregate read paths (FR-013/015/016/017/019/041, research D2/D4). Two
/// fixtures: a CLEAN one (every node ≥4, so nothing is suppressed) drives the happy-path guarantees —
/// dual live/snapshot agreement, reconciliation, trend, scope matrix, no-individual-field, timing; a THIN
/// one (an org-of-7 BU and a sub-4 single-child branch) drives suppression, including the round-2 red-team
/// cross-level differencing defence (BR1). Every test is <c>Category=PrivacyReporting</c>.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class RollupDashboardTests
{
    private readonly HapApiFactory _factory;

    public RollupDashboardTests(HapApiFactory factory) => _factory = factory;

    // ---- wire DTOs (camelCase; figures null when suppressed) -----------------------------------------
    private sealed record FiguresDto(
        int N,
        Dictionary<string, double> PerDimensionMean,
        Dictionary<int, int> FloorLevelDistribution,
        double CompletionPct,
        double UnmoderatedPct);

    private sealed record DimMetaDto(string Key, string Name, int DisplayOrder);

    private sealed record TrendDto(
        Guid CycleId, string CycleName, bool Suppressed, string? SuppressionReason, FiguresDto? Figures);

    private sealed record DashboardDto(
        string NodeType,
        Guid? NodeRef,
        string NodeName,
        Guid CycleId,
        string CycleName,
        string CycleState,
        bool Live,
        bool Suppressed,
        string? SuppressionReason,
        FiguresDto? Figures,
        List<DimMetaDto> Dimensions,
        List<TrendDto> Trend,
        object? Initiatives);

    private sealed record Fixture(
        DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed, (string Manager, string[] Reports)[] Teams);

    // ---- CLEAN fixture: every node ≥ 4, nothing suppressed ------------------------------------------
    // ROOT[Exec] → PFLEAD[PortLdr P1] → GRPLEAD_A[GrpLdr A] / GRPLEAD_B[GrpLdr B]; BU leads under the group
    // leaders; a manager of 4 employees per BU. BU_A,BU_B in Group A; BU_C in Group B; all Portfolio 1.
    private static Fixture CleanFixture()
    {
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group B", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("ROOT", "BU_A"),
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "ROOT"),
            Snap.Person("GRPLEAD_A", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("GRPLEAD_B", "BU_C", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("BULEAD_B", "BU_B", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("BULEAD_C", "BU_C", managerExternalRef: "GRPLEAD_B"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("MGR_B", "BU_B", managerExternalRef: "BULEAD_B"),
            Snap.Person("MGR_C", "BU_C", managerExternalRef: "BULEAD_C"),
        };
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_A{i}", "BU_A", managerExternalRef: "MGR_A"));
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_B{i}", "BU_B", managerExternalRef: "MGR_B"));
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_C{i}", "BU_C", managerExternalRef: "MGR_C"));

        var teams = new[]
        {
            ("MGR_A", new[] { "EMP_A1", "EMP_A2", "EMP_A3", "EMP_A4" }),
            ("MGR_B", new[] { "EMP_B1", "EMP_B2", "EMP_B3", "EMP_B4" }),
            ("MGR_C", new[] { "EMP_C1", "EMP_C2", "EMP_C3", "EMP_C4" }),
        };
        return new Fixture(bus, people.ToArray(), SeedFor(people), teams);
    }

    // ---- THIN fixture: an org-of-7 BU (team 4 + team 3) and a sub-4 single-child branch (BU_C=3) ------
    // Drives suppression: team N<4, complement, sub-4 BU, and the cross-level differencing attack.
    private static Fixture ThinFixture()
    {
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group B", portfolio: "Portfolio 2"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("ROOT", "BU_A"),
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "ROOT"),
            Snap.Person("GRPLEAD", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD"),
            Snap.Person("BULEAD_B", "BU_B", managerExternalRef: "GRPLEAD"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("MGR_A2", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("MGR_B", "BU_B", managerExternalRef: "BULEAD_B"),
            Snap.Person("HEAD_C", "BU_C", managerExternalRef: "ROOT"),
        };
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_A{i}", "BU_A", managerExternalRef: "MGR_A"));
        for (var i = 5; i <= 7; i++) people.Add(Snap.Person($"EMP_A{i}", "BU_A", managerExternalRef: "MGR_A2"));
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_B{i}", "BU_B", managerExternalRef: "MGR_B"));
        for (var i = 1; i <= 3; i++) people.Add(Snap.Person($"EMP_C{i}", "BU_C", managerExternalRef: "HEAD_C"));

        var teams = new[]
        {
            ("MGR_A", new[] { "EMP_A1", "EMP_A2", "EMP_A3", "EMP_A4" }),
            ("MGR_A2", new[] { "EMP_A5", "EMP_A6", "EMP_A7" }),
            ("MGR_B", new[] { "EMP_B1", "EMP_B2", "EMP_B3", "EMP_B4" }),
            ("HEAD_C", new[] { "EMP_C1", "EMP_C2", "EMP_C3" }),
        };
        return new Fixture(bus, people.ToArray(), SeedFor(people), teams);
    }

    private static SeedUserRecord[] SeedFor(IEnumerable<DirectoryPerson> people)
    {
        var seed = new List<SeedUserRecord>
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("ROOT", role: "HIG Executive"),
        };
        seed.AddRange(people
            .Where(p => p.ExternalRef is not ("ADMIN" or "ROOT"))
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));
        return seed.ToArray();
    }

    // ---- harness -------------------------------------------------------------------------------------
    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync(Fixture fx)
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(fx.Bus, fx.People));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            foreach (var bu in await db.BusinessUnits.ToListAsync())
            {
                bu.SetOnboarded(true);
            }
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(fx.Seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid fvId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(fvId, name, true));
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

    private async Task SubmitUniformAsync(string externalRef, int score)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var entries = form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList();
        await client.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task ModerateUniformAsync(string managerRef, string subjectRef, int managerScore)
    {
        var subjectId = await PersonIdAsync(subjectRef);
        var assessmentId = await AssessmentIdAsync(subjectRef);
        var manager = await ClientAsync(managerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{subjectId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, managerScore, "ok")).ToList();
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions))).StatusCode);
    }

    /// <summary>Every employee submits + is moderated to a uniform score, so the scored population is fully
    /// moderated (no auto-adoption at close) and the live path equals the frozen snapshot exactly.</summary>
    private async Task SubmitAndModerateAllAsync(Fixture fx, int score)
    {
        foreach (var (_, reports) in fx.Teams)
        {
            foreach (var r in reports)
            {
                await SubmitUniformAsync(r, score);
            }
        }
        foreach (var (manager, reports) in fx.Teams)
        {
            foreach (var r in reports)
            {
                await ModerateUniformAsync(manager, r, score);
            }
        }
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<Guid> AssessmentIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        return await db.Set<Assessment>().Where(a => a.PersonId == personId)
            .OrderByDescending(a => a.CreatedAt).Select(a => a.Id).FirstAsync();
    }

    private async Task<Guid> BuIdAsync(string code)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.BusinessUnits.SingleAsync(b => b.Code == code)).Id;
    }

    private async Task<Guid> GroupIdAsync(string name)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.Groups.SingleAsync(g => g.Name == name)).Id;
    }

    // ==================================================================================================
    // Dual path + reconciliation (CLEAN fixture)
    // ==================================================================================================

    [Fact]
    public async Task Live_open_cycle_and_frozen_snapshot_agree_for_a_just_closed_cycle()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var live = (await (await lead.GetAsync($"/api/bus/{buA}/dashboard")).Content.ReadFromJsonAsync<DashboardDto>())!;
        Assert.True(live.Live);
        Assert.False(live.Suppressed);
        Assert.NotNull(live.Figures);

        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        var snap = (await (await lead.GetAsync($"/api/bus/{buA}/dashboard")).Content.ReadFromJsonAsync<DashboardDto>())!;
        Assert.False(snap.Live);
        Assert.False(snap.Suppressed);
        Assert.NotNull(snap.Figures);

        Assert.Equal(4, snap.Figures!.N);
        Assert.Equal(live.Figures!.N, snap.Figures.N);
        Assert.Equal(live.Figures.CompletionPct, snap.Figures.CompletionPct, 6);
        Assert.Equal(0d, snap.Figures.UnmoderatedPct);
        foreach (var kv in live.Figures.PerDimensionMean)
        {
            Assert.Equal(kv.Value, snap.Figures.PerDimensionMean[kv.Key], 6);
        }
        foreach (var kv in live.Figures.FloorLevelDistribution)
        {
            Assert.Equal(kv.Value, snap.Figures.FloorLevelDistribution[kv.Key]);
        }
    }

    [Fact]
    public async Task Dashboard_figure_reconciles_to_the_raw_moderated_scores()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        var buA = await BuIdAsync("BU_A");
        var dash = (await (await (await ClientAsync("BULEAD_A")).GetAsync($"/api/bus/{buA}/dashboard"))
            .Content.ReadFromJsonAsync<DashboardDto>())!;

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var firstDim = await db.Dimensions.OrderBy(d => d.DisplayOrder).FirstAsync();
        var buAPeople = await db.People.Where(p => p.BusinessUnitId == buA).Select(p => p.Id).ToListAsync();
        var rawManagerScores = await db.Set<AssessmentScore>()
            .Where(s => s.DimensionId == firstDim.Id && s.ManagerScore != null)
            .Join(db.Set<Assessment>().Where(a => a.CycleId == cycleId && buAPeople.Contains(a.PersonId)),
                s => s.AssessmentId, a => a.Id, (s, a) => s.ManagerScore!.Value)
            .ToListAsync();

        Assert.Equal(4, rawManagerScores.Count);
        Assert.Equal(MaturityScoring.Mean(rawManagerScores), dash.Figures!.PerDimensionMean[firstDim.Key], 6);
        Assert.Equal(2.0, dash.Figures.PerDimensionMean[firstDim.Key], 6);
        Assert.Equal(4, dash.Figures.FloorLevelDistribution[2]);
    }

    [Fact]
    public async Task Trend_returns_per_dimension_means_across_all_closed_cycles()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);

        var c1 = await CreateAndOpenCycleAsync(admin, fvId, "2026-07");
        await SubmitAndModerateAllAsync(fx, 1);
        await admin.PostAsync($"/api/cycles/{c1}/close", null);

        await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        await SubmitAndModerateAllAsync(fx, 3);

        var buA = await BuIdAsync("BU_A");
        var dash = (await (await (await ClientAsync("BULEAD_A")).GetAsync($"/api/bus/{buA}/dashboard"))
            .Content.ReadFromJsonAsync<DashboardDto>())!;

        Assert.True(dash.Live);
        var firstDimKey = dash.Dimensions.OrderBy(d => d.DisplayOrder).First().Key;
        Assert.Equal(3.0, dash.Figures!.PerDimensionMean[firstDimKey], 6);

        Assert.Single(dash.Trend);
        Assert.Equal("2026-07", dash.Trend[0].CycleName);
        Assert.False(dash.Trend[0].Suppressed);
        Assert.Equal(1.0, dash.Trend[0].Figures!.PerDimensionMean[firstDimKey], 6);
    }

    // ==================================================================================================
    // Scope enforcement (role matrix) — CLEAN fixture; out of scope → 404
    // ==================================================================================================

    [Fact]
    public async Task Scope_matrix_bu_group_portfolio_executive_and_plain_manager()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var buC = await BuIdAsync("BU_C");
        var groupA = await GroupIdAsync("Group A");
        var groupB = await GroupIdAsync("Group B");

        async Task<HttpStatusCode> Get(string who, string path) =>
            (await (await ClientAsync(who)).GetAsync(path)).StatusCode;

        // BU Lead A → BU_A only.
        Assert.Equal(HttpStatusCode.OK, await Get("BULEAD_A", $"/api/bus/{buA}/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("BULEAD_A", $"/api/bus/{buB}/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("BULEAD_A", $"/api/bus/{buC}/dashboard"));

        // Group Leader A → both Group-A BUs + the group rollup, not Group B / BU_C.
        Assert.Equal(HttpStatusCode.OK, await Get("GRPLEAD_A", $"/api/bus/{buA}/dashboard"));
        Assert.Equal(HttpStatusCode.OK, await Get("GRPLEAD_A", $"/api/bus/{buB}/dashboard"));
        Assert.Equal(HttpStatusCode.OK, await Get("GRPLEAD_A", $"/api/org/group/{groupA}/rollup"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("GRPLEAD_A", $"/api/org/group/{groupB}/rollup"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("GRPLEAD_A", $"/api/bus/{buC}/dashboard"));

        // HIG Executive → all-HIG + any node.
        Assert.Equal(HttpStatusCode.OK, await Get("ROOT", "/api/org/allhig/rollup"));
        Assert.Equal(HttpStatusCode.OK, await Get("ROOT", $"/api/bus/{buC}/dashboard"));
        Assert.Equal(HttpStatusCode.OK, await Get("ROOT", $"/api/org/group/{groupB}/rollup"));
        // …but a NONEXISTENT node id is 404, not a hollow suppressed 200 (existence-leak convention).
        Assert.Equal(HttpStatusCode.NotFound, await Get("ROOT", $"/api/bus/{Guid.NewGuid()}/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("ROOT", $"/api/org/group/{Guid.NewGuid()}/rollup"));

        // Plain manager / individual → no BU or all-HIG (aggregates above their team).
        Assert.Equal(HttpStatusCode.NotFound, await Get("MGR_A", $"/api/bus/{buA}/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("MGR_A", "/api/org/allhig/rollup"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("EMP_A1", $"/api/bus/{buA}/dashboard"));
    }

    [Fact]
    public async Task Default_dashboard_resolves_each_persona_to_their_own_scope()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        async Task<DashboardDto> Default(string who) =>
            (await (await (await ClientAsync(who)).GetAsync("/api/me/dashboard")).Content.ReadFromJsonAsync<DashboardDto>())!;

        Assert.Equal("AllHig", (await Default("ROOT")).NodeType);        // Executive → all-HIG
        Assert.Equal("Portfolio", (await Default("PFLEAD")).NodeType);   // Portfolio Leader → their portfolio
        Assert.Equal("Group", (await Default("GRPLEAD_A")).NodeType);    // Group Leader → their group
        Assert.Equal("Bu", (await Default("BULEAD_A")).NodeType);        // BU Lead → their BU
        Assert.Equal("Team", (await Default("MGR_A")).NodeType);         // plain manager → their own team
    }

    // ==================================================================================================
    // Individual-score access is impossible; timing bound (CLEAN fixture)
    // ==================================================================================================

    [Fact]
    public async Task No_aggregate_endpoint_exposes_any_individual_field()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var buA = await BuIdAsync("BU_A");
        foreach (var path in new[] { $"/api/bus/{buA}/dashboard", "/api/org/allhig/rollup", "/api/me/dashboard" })
        {
            var raw = await (await (await ClientAsync("ROOT")).GetAsync(path)).Content.ReadAsStringAsync();
            foreach (var forbidden in new[] { "personId", "selfScore", "managerScore", "managerComment", "externalRef", "assessmentId" })
            {
                Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Dashboard_responds_well_under_five_seconds()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var sw = Stopwatch.StartNew();
        var response = await lead.GetAsync($"/api/bus/{buA}/dashboard");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"dashboard took {sw.ElapsedMilliseconds}ms (bound 5000ms)");
    }

    // ==================================================================================================
    // Suppression + cross-level differencing (THIN fixture) — a suppressed node emits NO number (F2/FR-071)
    // ==================================================================================================

    [Fact]
    public async Task A_sub_four_team_is_suppressed_with_no_figures_via_team_summary()
    {
        var fx = ThinFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        // Team(MGR_A2) = EMP_A5..7 = 3 → N<4, suppressed. NO figures reach the client.
        var suppressed = (await (await (await ClientAsync("MGR_A2")).GetAsync("/api/me/team/summary"))
            .Content.ReadFromJsonAsync<DashboardDto>())!;
        Assert.True(suppressed.Suppressed);
        Assert.Null(suppressed.Figures);
        Assert.Equal("N<4", suppressed.SuppressionReason);
    }

    [Fact]
    public async Task A_published_team_shows_figures_in_the_clean_fixture()
    {
        var fx = CleanFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var team = (await (await (await ClientAsync("MGR_A")).GetAsync("/api/me/team/summary"))
            .Content.ReadFromJsonAsync<DashboardDto>())!;
        Assert.False(team.Suppressed);
        Assert.NotNull(team.Figures);
        Assert.Equal(4, team.Figures!.N);
    }

    [Fact]
    public async Task A_sub_four_bu_is_suppressed_with_no_figures()
    {
        var fx = ThinFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var buC = await BuIdAsync("BU_C"); // scored population = EMP_C1..3 = 3
        var dash = (await (await (await ClientAsync("ROOT")).GetAsync($"/api/bus/{buC}/dashboard"))
            .Content.ReadFromJsonAsync<DashboardDto>())!;

        Assert.True(dash.Suppressed);
        Assert.Null(dash.Figures);
        Assert.NotEmpty(dash.Dimensions); // framework metadata is not aggregate data — still present
    }

    [Fact]
    public async Task Executive_cannot_recover_a_suppressed_sub4_branch_by_differencing()
    {
        // BR1: BU_A(7),BU_B(4) under Group A / Portfolio 1 (11); BU_C(3) under Group B / Portfolio 2; AllHig=14.
        // The old defence left Group A published (= its portfolio's 11 people), so AllHig(14) − GroupA(11) = 3
        // recovered the suppressed BU_C branch. The hierarchy-global defence must suppress Group A (and BU_B)
        // so no published node the Executive sees reveals the sub-4 branch.
        var fx = ThinFixture();
        var (admin, fvId) = await SeedAsync(fx);
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitAndModerateAllAsync(fx, 2);

        var root = await ClientAsync("ROOT");
        var buA = await BuIdAsync("BU_A");
        var buB = await BuIdAsync("BU_B");
        var buC = await BuIdAsync("BU_C");
        var groupA = await GroupIdAsync("Group A");

        async Task<DashboardDto> Read(string path) =>
            (await (await root.GetAsync(path)).Content.ReadFromJsonAsync<DashboardDto>())!;

        var allHig = await Read("/api/org/allhig/rollup");
        var gA = await Read($"/api/org/group/{groupA}/rollup");
        var cBu = await Read($"/api/bus/{buC}/dashboard");
        var aBu = await Read($"/api/bus/{buA}/dashboard");
        var bBu = await Read($"/api/bus/{buB}/dashboard");

        // The sub-4 branch is suppressed…
        Assert.True(cBu.Suppressed);
        Assert.Null(cBu.Figures);
        // …and the equal-membership Group A that used to reveal it is now suppressed too (no figures to subtract)…
        Assert.True(gA.Suppressed);
        Assert.Null(gA.Figures);
        // …as is BU_B, so BU_A + BU_B can't reconstruct Group A / Portfolio 1 either.
        Assert.True(bBu.Suppressed);
        Assert.Null(bBu.Figures);
        // The useful high-level total survives, and BU_A stays visible — but neither reveals BU_C by subtraction:
        // AllHig(14) − BU_A(7) = 7 = BU_B(4)+BU_C(3), a two-unknown sum, not the single suppressed figure 3.
        Assert.False(allHig.Suppressed);
        Assert.Equal(14, allHig.Figures!.N);
        Assert.False(aBu.Suppressed);
        Assert.Equal(7, aBu.Figures!.N);
    }
}
