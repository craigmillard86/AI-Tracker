using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Org;
using Hap.Domain.Register;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-15 BU capture forms: weekly AI-DLC declaration upsert + write authority (FR-047), monthly
/// Support/SOR metrics with YTD carry-forward (FR-048), and the declaration's measured-evidence panel
/// consumed from HAP-11's published <c>RollupReads</c> output (no new score query — see
/// <c>BuReportingEndpoints</c>'s class doc). Fixture mirrors <c>RegisterEndpointsTests</c>' engineered
/// hierarchy (ROOT[Exec]→PFLEAD[PortLdr]→GRPLEAD_A[GrpLdr]→BULEAD_A/_B[BuLead]→MGR_A/_B→EMPs), extended
/// with an audited <see cref="OrgRole.BuDelegate"/> grant holder (DELEGATE_A, homed in BU_B, delegated
/// over BU_A) so the write-scope tests exercise the grant path for real.
/// </summary>
[Collection("hap-db")]
public sealed class BuReportingEndpointsTests
{
    private readonly HapApiFactory _factory;

    public BuReportingEndpointsTests(HapApiFactory factory) => _factory = factory;

    // ---- wire DTOs -----------------------------------------------------------------------------------
    private sealed record DeclarationDto(
        Guid Id,
        Guid BusinessUnitId,
        DateOnly WeekOf,
        int DeclaredLevel,
        DateOnly? NextLevelExpectedDate,
        string RagStatus,
        string? Note,
        Guid DeclaredBy,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record FiguresDto(
        int N, Dictionary<string, double> PerDimensionMean, Dictionary<int, int> FloorLevelDistribution,
        double CompletionPct, double UnmoderatedPct);

    private sealed record MeasuredDto(
        string NodeType, Guid? NodeRef, string NodeName, Guid CycleId, string CycleName, string CycleState,
        bool Live, bool Suppressed, string? SuppressionReason, FiguresDto? Figures,
        List<object> Dimensions, List<object> Trend, object? Initiatives);

    private sealed record DeclarationsResponseDto(
        Guid BusinessUnitId, List<DeclarationDto> Declarations, MeasuredDto Measured,
        int? MeasuredFloorLevel, int? DeclaredVsMeasuredDivergence);

    private sealed record PostDeclarationBody(
        DateOnly WeekOf, int DeclaredLevel, DateOnly? NextLevelExpectedDate, string RagStatus, string? Note);

    private sealed record SupportInternalBody(decimal? TimeSavingsPct, string? FewerPeopleNeeded, string? SupportRatioImpact);

    private sealed record SupportCustomerBody(int? CustomersYtd, int? TicketsYtd, int? ResolvedByAiYtd, int? AiAssistedYtd);

    private sealed record PostMetricsBody(
        DateOnly Month, SupportInternalBody? SupportInternal, SupportCustomerBody? SupportCustomer, string? SorCalledByOtherApps);

    private sealed record MetricsResponseDto(
        Guid BusinessUnitId, DateOnly Month, SupportInternalBody SupportInternal, SupportCustomerBody SupportCustomer,
        string? SorCalledByOtherApps, bool CarriedForward, Guid? SubmittedBy, DateTime? CreatedAt);

    // ---- fixture: ROOT[Exec] → PFLEAD[PortLdr] → GRPLEAD_A[GrpLdr] → BULEAD_A/_B → MGR_A/_B → EMPs,
    // + DELEGATE_A (homed BU_B) holding an audited BuDelegate grant over BU_A -------------------------
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
            Snap.Person("DELEGATE_A", "BU_B", managerExternalRef: "MGR_B"),
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

    /// <summary>Full seed: directory sync + onboard, the audited BuDelegate grant, framework (needed for
    /// cycle creation in the evidence-panel tests). Returns an admin client + the active framework
    /// version id.</summary>
    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync()
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

            var buA = await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A");
            var delegatePerson = await db.People.SingleAsync(p => p.ExternalRef == "DELEGATE_A");
            db.RoleGrants.Add(RoleGrant.Create(delegatePerson.Id, OrgRole.BuDelegate, buA.Id, grantedBy: "test-seed"));
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seedResult = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seedResult!.VersionId);
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

    private async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid fvId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(fvId, name, true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return cycle.Id;
    }

    /// <summary>Submits + fully moderates one employee to a uniform <paramref name="score"/> across every
    /// dimension (mirrors <c>RollupDashboardTests.SubmitUniformAsync</c>/<c>ModerateUniformAsync</c>,
    /// collapsed into one call since this file only ever needs the moderated score of record).</summary>
    private async Task SubmitAndModerateAsync(string managerRef, string employeeRef, int score)
    {
        var employee = await ClientAsync(employeeRef);
        var form = (await (await employee.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var entries = form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList();
        await employee.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        Assert.Equal(HttpStatusCode.NoContent, (await employee.PostAsync("/api/me/assessment/submit", null)).StatusCode);

        var employeeId = await PersonIdAsync(employeeRef);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessmentId = await db.Set<Domain.Assessments.Assessment>()
            .Where(a => a.PersonId == employeeId).OrderByDescending(a => a.CreatedAt).Select(a => a.Id).FirstAsync();

        var manager = await ClientAsync(managerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{employeeId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, score, "ok")).ToList();
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions))).StatusCode);
    }

    /// <summary>Same as <see cref="SubmitAndModerateAsync"/> but scores PER DIMENSION (keyed by dimension
    /// key, via <paramref name="scoreFor"/>) instead of uniformly — needed to build a fixture where a
    /// person's per-dimension scores genuinely vary, which the modal-floor regression test below requires
    /// (a uniform per-person score makes floor-of-mean coincidentally equal the true floor, masking the bug
    /// it exists to catch).</summary>
    private async Task SubmitAndModerateByDimensionAsync(string managerRef, string employeeRef, Func<string, int> scoreFor)
    {
        var employee = await ClientAsync(employeeRef);
        var form = (await (await employee.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var entries = form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, scoreFor(d.Key), null)).ToList();
        await employee.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        Assert.Equal(HttpStatusCode.NoContent, (await employee.PostAsync("/api/me/assessment/submit", null)).StatusCode);

        var employeeId = await PersonIdAsync(employeeRef);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessmentId = await db.Set<Domain.Assessments.Assessment>()
            .Where(a => a.PersonId == employeeId).OrderByDescending(a => a.CreatedAt).Select(a => a.Id).FirstAsync();

        var manager = await ClientAsync(managerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{employeeId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, scoreFor(d.Key), "ok")).ToList();
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions))).StatusCode);
    }

    // ==================================================================================================
    // POST declarations — upsert + write scope (FR-047)
    // ==================================================================================================

    [Fact]
    public async Task Post_declaration_creates_new_row_for_bu_lead()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, new DateOnly(2026, 8, 15), "OnTrack", "steady progress");
        var resp = await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = (await resp.Content.ReadFromJsonAsync<DeclarationDto>())!;
        Assert.Equal(buA, dto.BusinessUnitId);
        Assert.Equal(new DateOnly(2026, 7, 20), dto.WeekOf); // normalised to the Monday of that week
        Assert.Equal(1, dto.DeclaredLevel);
        Assert.Equal("OnTrack", dto.RagStatus);
        Assert.Equal("steady progress", dto.Note);
    }

    [Fact]
    public async Task Post_declaration_same_week_resubmission_upserts()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var first = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "OnTrack", "first");
        var firstResp = await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", first);
        Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);

        // Same ISO week (a Thursday two days later, still week-of 2026-07-20) — resubmission, not a
        // new row: upserts and returns 200.
        var second = new PostDeclarationBody(new DateOnly(2026, 7, 24), 2, new DateOnly(2026, 9, 1), "AtRisk", "revised");
        var secondResp = await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", second);
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);

        var dto = (await secondResp.Content.ReadFromJsonAsync<DeclarationDto>())!;
        Assert.Equal(2, dto.DeclaredLevel);
        Assert.Equal("AtRisk", dto.RagStatus);
        Assert.Equal("revised", dto.Note);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var rows = await db.Set<Domain.Reporting.BuAiDlcDeclaration>()
            .Where(d => d.BusinessUnitId == buA && d.WeekOf == new DateOnly(2026, 7, 20)).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(2, rows[0].DeclaredLevel);
    }

    [Fact]
    public async Task Post_declaration_denied_for_non_bu_lead_non_delegate()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "OnTrack", null);

        // MGR_A is a plain manager under BU_A — has reports, but no BU Lead anchor and no BuDelegate grant.
        var mgr = await ClientAsync("MGR_A");
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);
    }

    [Fact]
    public async Task Post_declaration_denied_for_bu_lead_of_a_different_bu()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "OnTrack", null);

        var leadB = await ClientAsync("BULEAD_B");
        Assert.Equal(HttpStatusCode.Forbidden, (await leadB.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);
    }

    [Fact]
    public async Task Post_declaration_allowed_for_audited_bu_delegate_grant_holder()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "OnTrack", "via delegate");

        // DELEGATE_A is homed in BU_B but holds an explicit BuDelegate grant over BU_A.
        var delegateClient = await ClientAsync("DELEGATE_A");
        var resp = await delegateClient.PostAsJsonAsync($"/api/bus/{buA}/declarations", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Post_declaration_rejects_declared_level_out_of_range_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 4, null, "OnTrack", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);
    }

    // ==================================================================================================
    // GET declarations — history + measured-evidence panel [S] + divergence (FR-047)
    // ==================================================================================================

    [Fact]
    public async Task Get_declarations_returns_history_and_evidence_panel_with_divergence()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // BU_A's scored population: EMP_A1..4 moderated to 1,1,2,2 — every dimension's mean is uniformly
        // 1.5 (each employee's OWN score is the same across all their dimensions), so the measured floor
        // is floor(1.5) = 1 regardless of which dimension is "the minimum".
        await SubmitAndModerateAsync("MGR_A", "EMP_A1", 1);
        await SubmitAndModerateAsync("MGR_A", "EMP_A2", 1);
        await SubmitAndModerateAsync("MGR_A", "EMP_A3", 2);
        await SubmitAndModerateAsync("MGR_A", "EMP_A4", 2);

        // Also score BU_B's population (any values — this test only asserts BU_A's figures). Without this,
        // BU_B contributes zero to the shared Group/Portfolio/AllHig totals, making those totals EXACTLY
        // equal to BU_A's own N — a three-way tie (AllHig-chain / BU_A / Team(MGR_A), all N=4) in
        // HierarchySuppression.Close's cross-level differencing defence (HAP-11 BR1) that fires to stop
        // BU_B's suppressed 0 being recoverable by subtraction. The engine's tie-break among equal-count
        // candidates depends on internal node-array ordering (not deterministic test-to-test — it tracks
        // Dictionary/HashSet enumeration over freshly-generated GUIDs), so it can pick BU_A itself as the
        // sacrificed node instead of the intended top-level chain. Giving BU_B a real, non-suppressed
        // population removes the tie entirely (nothing left to protect via cross-level closure) — the
        // correct, deterministic fix belongs here in the fixture, not in the shared suppression engine.
        await SubmitAndModerateAsync("MGR_B", "EMP_B1", 1);
        await SubmitAndModerateAsync("MGR_B", "EMP_B2", 1);
        await SubmitAndModerateAsync("MGR_B", "EMP_B3", 2);
        await SubmitAndModerateAsync("MGR_B", "EMP_B4", 2);

        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        // Declare level 3 → divergence = 3 - 1 = 2.
        var declareBody = new PostDeclarationBody(new DateOnly(2026, 7, 22), 3, null, "OnTrack", null);
        Assert.Equal(HttpStatusCode.Created, (await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", declareBody)).StatusCode);

        var dto = (await (await lead.GetAsync($"/api/bus/{buA}/declarations")).Content.ReadFromJsonAsync<DeclarationsResponseDto>())!;

        Assert.Single(dto.Declarations);
        Assert.Equal(3, dto.Declarations[0].DeclaredLevel);
        Assert.False(dto.Measured.Suppressed);
        Assert.NotNull(dto.Measured.Figures);
        Assert.Equal(4, dto.Measured.Figures!.N);
        Assert.Equal(1, dto.MeasuredFloorLevel);
        Assert.Equal(2, dto.DeclaredVsMeasuredDivergence);
    }

    [Fact]
    public async Task Get_declarations_measured_floor_is_modal_per_person_floor_not_floor_of_mean()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // Three of BU_A's four employees each floor at 0 on a DIFFERENT dimension (everything else 3);
        // the fourth is uniformly 3. True per-person FloorLevelDistribution is {0: 3, 3: 1} — the MODAL
        // floor is 0. Floor-of-mean gets this wrong: every per-dimension mean is either 3 (nobody weak
        // there) or 2.25 (one of the four weak there), so floor(min-mean) = floor(2.25) = 2 — a population
        // AVERAGE masking three individually-weak people (root spec Appendix A floor rule; Q-033). This is
        // the HAP-15 domain-panel regression guard: it FAILS against the prior floor-of-mean computation
        // and PASSES against the corrected modal-from-distribution one.
        var dims = (await (await (await ClientAsync("EMP_A1")).GetAsync("/api/me/assessment"))
            .Content.ReadFromJsonAsync<SelfAssessmentResponse>())!.Dimensions
            .OrderBy(d => d.DisplayOrder).Select(d => d.Key).ToList();
        Assert.True(dims.Count >= 3, "fixture framework needs at least three dimensions for this test");

        await SubmitAndModerateByDimensionAsync("MGR_A", "EMP_A1", key => key == dims[0] ? 0 : 3);
        await SubmitAndModerateByDimensionAsync("MGR_A", "EMP_A2", key => key == dims[1] ? 0 : 3);
        await SubmitAndModerateByDimensionAsync("MGR_A", "EMP_A3", key => key == dims[2] ? 0 : 3);
        await SubmitAndModerateAsync("MGR_A", "EMP_A4", 3);

        // BU_B population, same reason as the divergence test above: avoids HierarchySuppression.Close's
        // non-deterministic cross-level tie-break (Q-032) by giving BU_B a real, non-suppressed population.
        await SubmitAndModerateAsync("MGR_B", "EMP_B1", 3);
        await SubmitAndModerateAsync("MGR_B", "EMP_B2", 3);
        await SubmitAndModerateAsync("MGR_B", "EMP_B3", 3);
        await SubmitAndModerateAsync("MGR_B", "EMP_B4", 3);

        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");
        var dto = (await (await lead.GetAsync($"/api/bus/{buA}/declarations")).Content.ReadFromJsonAsync<DeclarationsResponseDto>())!;

        Assert.False(dto.Measured.Suppressed);
        Assert.Equal(4, dto.Measured.Figures!.N);
        Assert.Equal(0, dto.MeasuredFloorLevel);
    }

    [Fact]
    public async Task Get_declarations_history_newest_first()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        // Two distinct weeks (not the same ISO week), so both persist as separate rows.
        await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations",
            new PostDeclarationBody(new DateOnly(2026, 7, 6), 1, null, "OnTrack", "week1"));
        await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations",
            new PostDeclarationBody(new DateOnly(2026, 7, 20), 2, null, "OnTrack", "week3"));

        var dto = (await (await lead.GetAsync($"/api/bus/{buA}/declarations")).Content.ReadFromJsonAsync<DeclarationsResponseDto>())!;

        Assert.Equal(2, dto.Declarations.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), dto.Declarations[0].WeekOf); // newest first
        Assert.Equal(new DateOnly(2026, 7, 6), dto.Declarations[1].WeekOf);
    }

    [Fact]
    public async Task Get_declarations_readable_by_group_leader_spanning_the_bu_broader_than_write_scope()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var buA = await BuIdAsync("BU_A");

        // GRPLEAD_A cannot WRITE (an above-BU leader is not a BU Lead or delegate)…
        var group = await ClientAsync("GRPLEAD_A");
        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "OnTrack", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await group.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);

        // …but CAN read (Group Leader spans BU_A — same scope as the BU dashboard).
        Assert.Equal(HttpStatusCode.OK, (await group.GetAsync($"/api/bus/{buA}/declarations")).StatusCode);
    }

    [Fact]
    public async Task Get_declarations_out_of_scope_caller_gets_404()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var buA = await BuIdAsync("BU_A");

        // BULEAD_B's scope is BU_B only — BU_A is out of scope entirely (not even readable).
        var leadB = await ClientAsync("BULEAD_B");
        Assert.Equal(HttpStatusCode.NotFound, (await leadB.GetAsync($"/api/bus/{buA}/declarations")).StatusCode);
    }

    // ==================================================================================================
    // POST/GET metrics — upsert + YTD carry-forward + SOR current-month-only (FR-048)
    // ==================================================================================================

    [Fact]
    public async Task Metrics_month_two_pre_populates_support_customer_ytd_from_month_one_and_sor_starts_empty()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var month1Body = new PostMetricsBody(
            new DateOnly(2026, 6, 1),
            new SupportInternalBody(12.5m, "2 fewer", "reduced backlog"),
            new SupportCustomerBody(100, 250, 60, 40),
            "Called by ClaimsPortal");
        var created = await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", month1Body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // No row yet for month 2 — GET should carry SupportCustomer YTD forward from month 1.
        var month2 = (await (await lead.GetAsync($"/api/bus/{buA}/metrics?month=2026-07-01"))
            .Content.ReadFromJsonAsync<MetricsResponseDto>())!;

        Assert.True(month2.CarriedForward);
        Assert.Equal(new DateOnly(2026, 7, 1), month2.Month);
        Assert.Equal(100, month2.SupportCustomer.CustomersYtd);
        Assert.Equal(250, month2.SupportCustomer.TicketsYtd);
        Assert.Equal(60, month2.SupportCustomer.ResolvedByAiYtd);
        Assert.Equal(40, month2.SupportCustomer.AiAssistedYtd);

        // SOR is current-month-only (FR-048) — never carried forward, even though month 1 had a value.
        Assert.Null(month2.SorCalledByOtherApps);
        // SupportInternal is also current-month-only — starts blank regardless of month 1's figures.
        Assert.Null(month2.SupportInternal.TimeSavingsPct);
        Assert.Null(month2.SupportInternal.FewerPeopleNeeded);
        Assert.Null(month2.SupportInternal.SupportRatioImpact);
    }

    [Fact]
    public async Task Metrics_month_with_no_row_and_no_prior_month_returns_blank_not_carried_forward()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var dto = (await (await lead.GetAsync($"/api/bus/{buA}/metrics?month=2026-01-01"))
            .Content.ReadFromJsonAsync<MetricsResponseDto>())!;

        Assert.False(dto.CarriedForward);
        Assert.Null(dto.SupportCustomer.CustomersYtd);
        Assert.Null(dto.SorCalledByOtherApps);
    }

    [Fact]
    public async Task Post_metrics_same_month_resubmission_upserts()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var first = new PostMetricsBody(
            new DateOnly(2026, 6, 3), new SupportInternalBody(5m, null, null), new SupportCustomerBody(10, 20, 5, 5), "AppA");
        Assert.Equal(HttpStatusCode.Created, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", first)).StatusCode);

        // Same calendar month (day differs, month normalises to first-of-month) — resubmission, 200.
        var second = new PostMetricsBody(
            new DateOnly(2026, 6, 20), new SupportInternalBody(9m, "1 fewer", null), new SupportCustomerBody(15, 25, 9, 9), "AppB");
        var secondResp = await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", second);
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var rows = await db.Set<Domain.Reporting.BuMonthlyMetrics>()
            .Where(m => m.BusinessUnitId == buA && m.Month == new DateOnly(2026, 6, 1)).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(15, rows[0].SupportCustomer.CustomersYtd);
        Assert.Equal("AppB", rows[0].SorCalledByOtherApps);
    }

    [Fact]
    public async Task Post_metrics_denied_for_non_bu_lead_non_delegate()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var mgr = await ClientAsync("MGR_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_denied_for_bu_lead_of_a_different_bu()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, null, null);

        var leadB = await ClientAsync("BULEAD_B");
        Assert.Equal(HttpStatusCode.Forbidden, (await leadB.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Get_metrics_denied_for_non_bu_lead_non_delegate()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");

        // Metrics GET reuses the narrower WRITE scope (BuReportingEndpoints class doc) — unlike
        // declarations GET, an above-BU leader or plain manager gets no read either.
        var mgr = await ClientAsync("MGR_A");
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.GetAsync($"/api/bus/{buA}/metrics?month=2026-06-01")).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_allowed_for_audited_bu_delegate_grant_holder()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var delegateClient = await ClientAsync("DELEGATE_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, new SupportCustomerBody(1, 1, 1, 1), null);
        Assert.Equal(HttpStatusCode.Created, (await delegateClient.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_rejects_negative_customers_ytd_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, new SupportCustomerBody(-1, null, null, null), null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }
}
