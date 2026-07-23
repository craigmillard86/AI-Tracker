using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// QA-window adversarial coverage for HAP-15 (fresh-instance QA pass, CLAUDE.md §9), added during QA
/// rather than Dev — attributed as QA work. Dev's own <see cref="BuReportingEndpointsTests"/> already
/// covers the happy-path upsert/authority/carry-forward behaviours; this file targets the boundary and
/// bypass angles that suite does not exercise: declared-level boundary values (not just the +1-over
/// case), a malformed/non-integer wire value, a route-vs-body buId forgery attempt, an out-of-scope
/// Individual (not merely a differently-scoped BU Lead) reading a foreign BU, the mandatory
/// sub-4-scored-population evidence-panel suppression check (CLAUDE.md §9.3(b) — the only privacy-
/// relevant surface this L2 story exposes, since it reads no Assessments/AssessmentScores table
/// directly, only HAP-11's already-suppressed <c>RollupReads</c> output), and the metrics numeric
/// invariants beyond the one field Dev's suite exercised (CustomersYtd).
/// </summary>
[Collection("hap-db")]
public sealed class BuReportingEndpointsQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public BuReportingEndpointsQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    private sealed record PostDeclarationBody(
        DateOnly WeekOf, int DeclaredLevel, DateOnly? NextLevelExpectedDate, string RagStatus, string? Note);

    private sealed record SupportCustomerBody(int? CustomersYtd, int? TicketsYtd, int? ResolvedByAiYtd, int? AiAssistedYtd);

    private sealed record SupportInternalBody(decimal? TimeSavingsPct, string? FewerPeopleNeeded, string? SupportRatioImpact);

    private sealed record PostMetricsBody(
        DateOnly Month, SupportInternalBody? SupportInternal, SupportCustomerBody? SupportCustomer, string? SorCalledByOtherApps);

    private sealed record DeclarationDto(Guid Id, Guid BusinessUnitId, DateOnly WeekOf, int DeclaredLevel);

    private sealed record FiguresDto(int N);

    private sealed record MeasuredDto(bool Suppressed, string? SuppressionReason, FiguresDto? Figures);

    private sealed record DeclarationsResponseDto(
        Guid BusinessUnitId, List<DeclarationDto> Declarations, MeasuredDto Measured,
        int? MeasuredFloorLevel, int? DeclaredVsMeasuredDivergence);

    /// <summary>
    /// Fixture: ROOT[Exec] → PFLEAD[PortLdr] → GRPLEAD_A[GrpLdr] → BULEAD_A/BULEAD_C[BuLead] → MGR_A/MGR_C
    /// → EMPs. BU_A carries a full 4-person scored population (avoids the same cross-level suppression
    /// tie-break non-determinism BuReportingEndpointsTests' own comment documents — Q-032 — by never
    /// leaving another BU at a suppressed zero when BU_C is under test). BU_C is the sub-4 target: only
    /// 3 scored employees, deliberately below the N&lt;4 threshold. EMP_A1 is a plain Individual with no
    /// hierarchy leadership role and no grant anywhere — the out-of-scope caller for the negative-read test.
    /// </summary>
    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync()
    {
        await _factory.ResetAsync();
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("ROOT", "BU_A"),
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "ROOT"),
            Snap.Person("GRPLEAD_A", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("BULEAD_C", "BU_C", managerExternalRef: "GRPLEAD_A"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("MGR_C", "BU_C", managerExternalRef: "BULEAD_C"),
        };
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"EMP_A{i}", "BU_A", managerExternalRef: "MGR_A"));
        for (var i = 1; i <= 3; i++) people.Add(Snap.Person($"EMP_C{i}", "BU_C", managerExternalRef: "MGR_C"));

        var seed = new List<SeedUserRecord>
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("ROOT", role: "HIG Executive"),
        };
        seed.AddRange(people
            .Where(p => p.ExternalRef is not ("ADMIN" or "ROOT"))
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people.ToArray()));
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

    private async Task<Guid> BuIdAsync(string code)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.BusinessUnits.SingleAsync(b => b.Code == code)).Id;
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid fvId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(fvId, name, true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return cycle.Id;
    }

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

    // ==================================================================================================
    // Declared-level boundary + malformed-wire attacks (FR-047)
    // ==================================================================================================

    [Fact]
    public async Task Post_declaration_rejects_declared_level_negative_one_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), -1, null, "OnTrack", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);
    }

    [Fact]
    public async Task Post_declaration_rejects_unrecognised_rag_status_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostDeclarationBody(new DateOnly(2026, 7, 22), 1, null, "SuperGreen", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await lead.PostAsJsonAsync($"/api/bus/{buA}/declarations", body)).StatusCode);
    }

    /// <summary>Non-integer <c>declaredLevel</c> on the wire — JSON model binding rejects it before the
    /// domain guard ever runs. Attack goal: confirm this fails CLOSED (400, not a 200/201 that silently
    /// coerces to 0, and not a 500 that could leak a stack trace) rather than the domain's own 422.</summary>
    [Fact]
    public async Task Post_declaration_rejects_non_integer_declared_level_without_500()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var raw = """{"weekOf":"2026-07-22","declaredLevel":"not-a-number","nextLevelExpectedDate":null,"ragStatus":"OnTrack","note":null}""";
        var resp = await lead.PostAsync($"/api/bus/{buA}/declarations",
            new StringContent(raw, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>Attack: forge a <c>businessUnitId</c> field into the POST body pointing at a different BU
    /// than the route, hoping the server reads the body's id instead of the route's. The wire contract
    /// (<see cref="PostDeclarationRequest"/>) has no such field at all — this proves the route id is the
    /// ONLY id ever consulted (structural proof, not just a runtime check) by confirming the extra field
    /// is silently ignored and the row lands under the ROUTE's BU.</summary>
    [Fact]
    public async Task Post_declaration_forged_business_unit_id_in_body_is_ignored_route_wins()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var buC = await BuIdAsync("BU_C");
        var lead = await ClientAsync("BULEAD_A");

        var raw = $$"""
            {"weekOf":"2026-07-22","declaredLevel":2,"nextLevelExpectedDate":null,"ragStatus":"OnTrack","note":null,"businessUnitId":"{{buC}}"}
            """;
        var resp = await lead.PostAsync($"/api/bus/{buA}/declarations",
            new StringContent(raw, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = (await resp.Content.ReadFromJsonAsync<DeclarationDto>())!;
        Assert.Equal(buA, dto.BusinessUnitId); // route wins — the forged BU_C id in the body had no effect

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False(await db.Set<Domain.Reporting.BuAiDlcDeclaration>().AnyAsync(d => d.BusinessUnitId == buC));
    }

    // ==================================================================================================
    // Out-of-scope read attack (a plain Individual, not just a foreign BU Lead) — FR-047
    // ==================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Get_declarations_denied_for_a_plain_individual_with_no_hierarchy_role_or_grant()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var buC = await BuIdAsync("BU_C");

        // EMP_A1 is a rank-and-file Individual: no BU Lead/Group/Portfolio anchor, no BuDelegate grant,
        // homed in a DIFFERENT BU than the one addressed. Broader-than-write read scope (Group/Portfolio
        // Leader, HIG Executive) must not accidentally catch a plain Individual too.
        var outsider = await ClientAsync("EMP_A1");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/bus/{buC}/declarations")).StatusCode);
    }

    // ==================================================================================================
    // Mandatory §9.3(b): the sub-4-scored-population evidence panel must suppress (FR-047/FR-071)
    // ==================================================================================================

    /// <summary>The mandatory <4-aggregate attempt for this story (CLAUDE.md §9.3(b)). BU_C has only 3
    /// scored employees — one short of the N&lt;4 threshold. Attack: read the declaration GET's evidence
    /// panel as BU_C's own BU Lead (fully in-scope) and confirm NO measured number of any kind — n,
    /// per-dimension mean, floor distribution, measured floor level — reaches the wire. There is no
    /// direct Assessments/AssessmentScores read anywhere in this story (confirmed by inspection of
    /// BuReportingEndpoints — the evidence panel is entirely sourced from HAP-11's already-suppressed
    /// RollupReads.ReadBuDashboardAsync), so this is the one privacy-relevant surface this L2 story has;
    /// no cross-role individual-score sweep applies here (documented in the story file's QA section).</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Get_declarations_evidence_panel_suppressed_for_bu_with_three_scored_people()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        // BU_A gets its full 4-person population scored (avoids the documented cross-level suppression
        // tie-break non-determinism — Q-032 — from leaving BU_A at a suppressed zero while BU_C is under
        // test).
        await SubmitAndModerateAsync("MGR_A", "EMP_A1", 2);
        await SubmitAndModerateAsync("MGR_A", "EMP_A2", 2);
        await SubmitAndModerateAsync("MGR_A", "EMP_A3", 2);
        await SubmitAndModerateAsync("MGR_A", "EMP_A4", 2);

        // BU_C: only 3 of a possible population scored — deliberately below N<4.
        await SubmitAndModerateAsync("MGR_C", "EMP_C1", 3);
        await SubmitAndModerateAsync("MGR_C", "EMP_C2", 3);
        await SubmitAndModerateAsync("MGR_C", "EMP_C3", 3);

        var buC = await BuIdAsync("BU_C");
        var lead = await ClientAsync("BULEAD_C");

        // A declaration exists too (the BU Lead's OWN input, unrelated to measured data) — confirms
        // suppression is specific to the MEASURED side, not a blanket 404/empty response.
        var declareBody = new PostDeclarationBody(new DateOnly(2026, 7, 22), 2, null, "OnTrack", null);
        Assert.Equal(HttpStatusCode.Created, (await lead.PostAsJsonAsync($"/api/bus/{buC}/declarations", declareBody)).StatusCode);

        var dto = (await (await lead.GetAsync($"/api/bus/{buC}/declarations")).Content.ReadFromJsonAsync<DeclarationsResponseDto>())!;

        Assert.True(dto.Measured.Suppressed);
        Assert.Null(dto.Measured.Figures); // no N, no per-dimension mean, no floor distribution
        Assert.Null(dto.MeasuredFloorLevel); // no leaked floor even though 3 real scores exist server-side
        Assert.Null(dto.DeclaredVsMeasuredDivergence); // divergence needs a measured floor — also suppressed
        Assert.Single(dto.Declarations); // the BU's own declaration is NOT suppressed — it isn't measured data
    }

    // ==================================================================================================
    // Metrics numeric invariants beyond CustomersYtd (FR-048)
    // ==================================================================================================

    [Fact]
    public async Task Post_metrics_rejects_negative_tickets_ytd_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, new SupportCustomerBody(null, -5, null, null), null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_rejects_negative_resolved_by_ai_ytd_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, new SupportCustomerBody(null, null, -1, null), null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_rejects_negative_ai_assisted_ytd_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(new DateOnly(2026, 6, 1), null, new SupportCustomerBody(null, null, null, -1), null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_rejects_time_savings_pct_over_100_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(
            new DateOnly(2026, 6, 1), new SupportInternalBody(150m, null, null), null, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }

    [Fact]
    public async Task Post_metrics_rejects_time_savings_pct_negative_with_422()
    {
        await SeedAsync();
        var buA = await BuIdAsync("BU_A");
        var lead = await ClientAsync("BULEAD_A");

        var body = new PostMetricsBody(
            new DateOnly(2026, 6, 1), new SupportInternalBody(-1m, null, null), null, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await lead.PostAsJsonAsync($"/api/bus/{buA}/metrics", body)).StatusCode);
    }
}
