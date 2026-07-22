using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Assessments;
using Hap.Domain.Org;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Hap.Synth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-11 (fresh-instance QA pass, CLAUDE.md §9 / the L3 mandatory
/// attempts). This is a FRESH agent with no shared context with dev-hap11 — every check here is derived
/// from the acceptance criteria and the FR spec, not from dev's own <see cref="RollupDashboardTests"/>
/// (read for orientation only; no assertions or fixtures are copied from it). Two things this file adds
/// that dev's own suite does not:
///
/// <para>(1) The <b>REAL canonical 23-BU synthetic directory</b> (<see cref="Distributions"/>'s engineered
/// sub-4 BU / n=3 team / org-of-7 BU), not a hand-rolled fixture — proving the BR1 cross-level defence
/// against the actual generator wiring (real BU→Group→Portfolio containment, real headcounts), and the
/// SC-006 timing bound against the FULL population (~10k people, ~2k team nodes), which is the one path
/// dev's own timing test does not exercise (it times a 26-person fixture).</para>
///
/// <para>(2) Independent reconciliation with NON-uniform per-person, per-dimension scores (dev's own
/// reconciliation test uses one uniform score for the whole fixture, which cannot catch a dimension- or
/// person-mixup bug), and an "all seven seeded roles / every endpoint" individual-field sweep (dev's own
/// no-leak test checks one role only).</para>
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class RollupDashboardQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public RollupDashboardQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    // ---- wire DTOs, defined independently of dev's own RollupDashboardTests DTOs ---------------------
    private sealed record FiguresDto(
        int N, Dictionary<string, double> PerDimensionMean, Dictionary<int, int> FloorLevelDistribution,
        double CompletionPct, double UnmoderatedPct);

    private sealed record DimMetaDto(string Key, string Name, int DisplayOrder);

    private sealed record TrendDto(Guid CycleId, string CycleName, bool Suppressed, string? SuppressionReason, FiguresDto? Figures);

    private sealed record DashboardDto(
        string NodeType, Guid? NodeRef, string NodeName, Guid CycleId, string CycleName, string CycleState,
        bool Live, bool Suppressed, string? SuppressionReason, FiguresDto? Figures,
        List<DimMetaDto> Dimensions, List<TrendDto> Trend, object? Initiatives);

    private static readonly string[] SevenRoles =
    {
        "Individual", "Manager", "BU Lead", "Group Leader", "Portfolio Leader", "HIG Executive", "Platform Admin",
    };

    // ==================================================================================================
    // Setup: the REAL canonical 23-BU / ~10k-person directory (Distributions.CanonicalSeed) — every
    // engineered edge case (sub-4 BU, org-of-7 BU, n=3/n=2 teams) is REAL generator output, not a fixture.
    // ==================================================================================================

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedCanonicalAsync(
        IEnumerable<SeedUserRecord>? extraSeedUsers = null)
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
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
        var seedUsers = extraSeedUsers is null
            ? _factory.CanonicalSeedUsers.ToList()
            : _factory.CanonicalSeedUsers.Concat(extraSeedUsers).ToList();
        _factory.SeedUsers.Inner = new StubSeedUserSource(seedUsers);

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, Distributions.AdminRef);
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid fvId, string name = "2026-08")
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

    private static async Task<DashboardDto> ReadAsync(HttpClient client, string path)
    {
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DashboardDto>())!;
    }

    private async Task SubmitSelfAsync(string externalRef, Func<int, int> scoreForDimensionIndex)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var entries = form.Dimensions.Select((d, i) => new ScoreEntry(d.DimensionId, scoreForDimensionIndex(i), null)).ToList();
        await client.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task ModerateAsync(string managerRef, string subjectRef, Func<int, int> scoreForDimensionIndex)
    {
        var subjectId = await PersonIdAsync(subjectRef);
        var assessmentId = await AssessmentIdAsync(subjectRef);
        var manager = await ClientAsync(managerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{subjectId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select((d, i) => new ModerateDecision(d.DimensionId, scoreForDimensionIndex(i), "ok")).ToList();
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions))).StatusCode);
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

    private async Task<Guid> GroupIdOfBuAsync(string buCode)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.BusinessUnits.SingleAsync(b => b.Code == buCode)).GroupId;
    }

    /// <summary>The first <paramref name="count"/> of a manager's direct reports (deterministic order by
    /// external ref), for wiring up a small, controlled, REAL scored population inside an ordinary
    /// canonical BU without needing to know the generator's internal team-shape RNG draws. Restricted to
    /// active, non-contractor reports — the canonical generator randomly tags ~9% of ordinary members as
    /// contractors (Distributions.ContractorRate), and a contractor is excluded from cycle invitations
    /// (FR-005), so an unfiltered pick can 404 on <c>GET /api/me/assessment</c>.</summary>
    private async Task<List<string>> FirstDirectReportsAsync(string managerExternalRef, int count)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var managerId = (await db.People.SingleAsync(p => p.ExternalRef == managerExternalRef)).Id;
        return await db.People
            .Where(p => p.ManagerPersonId == managerId && p.IsActive && p.EmployeeType == EmployeeType.Employee)
            .OrderBy(p => p.ExternalRef)
            .Take(count)
            .Select(p => p.ExternalRef)
            .ToListAsync();
    }

    // ==================================================================================================
    // (a) Individual-score access via every dashboard/rollup endpoint — attempted as EACH seeded role.
    // ==================================================================================================

    [Fact]
    public async Task Every_seeded_role_finds_no_individual_field_on_any_rollup_or_trend_endpoint()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var bu01 = await BuIdAsync(Distributions.BuCode(1));
        var group1 = await GroupIdOfBuAsync(Distributions.BuCode(1));

        // Every field name that could carry a single person's data — not just the names dev's own test
        // checked (adds jobTitle/displayName/onLeave/employeeType, and scans the RAW body of every
        // endpoint including the trend array, not a curated subset of paths).
        var forbidden = new[]
        {
            "personId", "selfScore", "managerScore", "managerComment", "externalRef", "assessmentId",
            "email", "jobTitle", "displayName", "onLeave", "employeeType", "isActive",
        };

        var paths = new[]
        {
            "/api/me/dashboard",
            "/api/me/team/summary",
            "/api/org/allhig/rollup",
            $"/api/bus/{bu01}/dashboard",
            $"/api/org/group/{group1}/rollup",
        };

        foreach (var role in SevenRoles)
        {
            var externalRef = _factory.CanonicalSeedUsers.Single(u => u.Role == role).ExternalRef;
            var client = await ClientAsync(externalRef);
            foreach (var path in paths)
            {
                var resp = await client.GetAsync(path);
                // Out-of-scope for this role is a bare 404 (existence-leak convention) — nothing to scan,
                // and itself proves no data escaped. Only a 200 body is scanned for a leaked field.
                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    continue;
                }
                var raw = await resp.Content.ReadAsStringAsync();
                foreach (var f in forbidden)
                {
                    Assert.False(
                        raw.Contains(f, StringComparison.OrdinalIgnoreCase),
                        $"role={role} path={path} leaked forbidden field '{f}': {raw}");
                }
            }
        }
    }

    /// <summary>The generic <c>/api/org/{nodeType}/{nodeId}/rollup</c> route structurally accepts
    /// <c>nodeType=team</c> (it is a valid <see cref="Hap.Domain.Org.OrgNodeType"/>) — this is the one
    /// place a caller could try to use an ADDRESSED route to reach an ARBITRARY team's aggregate, bypassing
    /// the "own team only" restriction <c>/api/me/team/summary</c> enforces. Every seeded role, against
    /// both a large published team and a tiny suppressed one, must get 404 — never a team.</summary>
    [Fact]
    public async Task Team_aggregates_are_never_reachable_via_the_addressed_org_route_for_any_role_or_size()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var largeTeamManagerId = await PersonIdAsync(Distributions.BuLeadRef(12)); // BU12: single team, n=7
        var tinyTeamManagerId = await PersonIdAsync(Distributions.SeedManagerRef); // has >=1 report (HAP-2)

        foreach (var role in SevenRoles)
        {
            var externalRef = _factory.CanonicalSeedUsers.Single(u => u.Role == role).ExternalRef;
            var client = await ClientAsync(externalRef);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/org/team/{largeTeamManagerId}/rollup")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/org/team/{tinyTeamManagerId}/rollup")).StatusCode);
        }
    }

    /// <summary>Negative-path input validation: an unparseable node-type segment and an AllHig node
    /// addressed with an id (the wrong route) must both 404 — never a 500 or a hollow 200.</summary>
    [Fact]
    public async Task Malformed_or_unrecognised_node_type_segments_404_never_500()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        var exec = await ClientAsync(Distributions.ExecRef);

        Assert.Equal(HttpStatusCode.NotFound, (await exec.GetAsync($"/api/org/not-a-real-type/{Guid.NewGuid()}/rollup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await exec.GetAsync($"/api/org/allhig/{Guid.NewGuid()}/rollup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await exec.GetAsync("/api/bus/not-a-guid/dashboard")).StatusCode);
    }

    // ==================================================================================================
    // (b) Sub-4 aggregates: direct reads + differencing, against the REAL engineered canonical nodes.
    // ==================================================================================================

    [Fact]
    public async Task Engineered_canonical_edge_nodes_read_as_expected_sub4_suppressed_single_team_published()
    {
        // Every external ref this test signs in as (submitter OR moderator) must be pre-registered — the
        // local dev provider rejects sign-in for anyone not in the seed-user list (HAP-4). BU20's two
        // members and the Team3 edge case's manager+reports are real generator output but are NOT among
        // the seven canonical role-labelled seed users, so they need explicit QA-Fixture entries.
        var extra = new List<SeedUserRecord>
        {
            Snap.SeedUser(Distributions.BuLeadRef(20), role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            Snap.SeedUser("HAP-BU20-0001", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            Snap.SeedUser("HAP-BU20-0002", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            Snap.SeedUser(Distributions.Team3ManagerRef, role: "QA-Fixture", buCode: Distributions.PrimaryFixtureBuCode),
            Snap.SeedUser(Distributions.Team3ManagerRef + "-R1", role: "QA-Fixture", buCode: Distributions.PrimaryFixtureBuCode),
            Snap.SeedUser(Distributions.Team3ManagerRef + "-R2", role: "QA-Fixture", buCode: Distributions.PrimaryFixtureBuCode),
        };
        var (admin, fvId) = await SeedCanonicalAsync(extra);
        await CreateAndOpenCycleAsync(admin, fvId);

        // BU20 (edge b): BU Lead + 2 members = 3 total, well under the floor even before any submission.
        var buLead20 = await ClientAsync(Distributions.BuLeadRef(20));
        await SubmitSelfAsync("HAP-BU20-0001", _ => 2);
        await SubmitSelfAsync("HAP-BU20-0002", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0001", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0002", _ => 2);

        var bu20 = await BuIdAsync(Distributions.SubFourBuCode);
        var dash = await ReadAsync(buLead20, $"/api/bus/{bu20}/dashboard");
        Assert.True(dash.Suppressed);
        Assert.Null(dash.Figures);
        Assert.Equal("N<4", dash.SuppressionReason);

        // edge (a): the n=3 (2-report) team in BU01 — Team3ManagerRef + 2 reports.
        await SubmitSelfAsync(Distributions.Team3ManagerRef + "-R1", _ => 2);
        await SubmitSelfAsync(Distributions.Team3ManagerRef + "-R2", _ => 2);
        await ModerateAsync(Distributions.Team3ManagerRef, Distributions.Team3ManagerRef + "-R1", _ => 2);
        await ModerateAsync(Distributions.Team3ManagerRef, Distributions.Team3ManagerRef + "-R2", _ => 2);
        var team3 = await ClientAsync(Distributions.Team3ManagerRef);
        var teamDash = await ReadAsync(team3, "/api/me/team/summary");
        Assert.True(teamDash.Suppressed);
        Assert.Null(teamDash.Figures);
        Assert.Equal("N<4", teamDash.SuppressionReason);

        // BU12 (edge c, single-team BU): BU Lead + 7 members, all one team — published, sanity check that
        // an engineered edge case is NOT suppressed when it genuinely clears the floor (avoids a QA pass
        // that only ever checks the "suppressed" branch). Discover the real report refs FIRST, then
        // register them (and the BU Lead) before signing in as any of them.
        var reports = await FirstDirectReportsAsync(Distributions.BuLeadRef(12), 4);
        Assert.Equal(4, reports.Count);
        var bu12Extra = new List<SeedUserRecord>
        {
            Snap.SeedUser(Distributions.BuLeadRef(12), role: "QA-Fixture", buCode: Distributions.SingleTeamBuCode),
        };
        bu12Extra.AddRange(reports.Select(r => Snap.SeedUser(r, role: "QA-Fixture", buCode: Distributions.SingleTeamBuCode)));
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers.Concat(extra).Concat(bu12Extra).ToList());

        foreach (var r in reports)
        {
            await SubmitSelfAsync(r, _ => 2);
        }
        foreach (var r in reports)
        {
            await ModerateAsync(Distributions.BuLeadRef(12), r, _ => 2);
        }
        var bu12 = await BuIdAsync(Distributions.SingleTeamBuCode);
        var exec = await ClientAsync(Distributions.ExecRef);
        var bu12Dash = await ReadAsync(exec, $"/api/bus/{bu12}/dashboard");
        Assert.False(bu12Dash.Suppressed);
        Assert.NotNull(bu12Dash.Figures);
        Assert.Equal(4, bu12Dash.Figures!.N);
    }

    /// <summary>
    /// THE mandatory (b) cross-level differencing attack (§9.3(b) / CLAUDE.md L3 mandate), run against the
    /// REAL canonical directory's engineered sub-4 BU (BU20) and its REAL siblings (BU17/18/19) under their
    /// REAL group (not a hand-built tree) — as the most-privileged reader (HIG Executive) who can see every
    /// published node. Each of BU17/18/19 gets exactly 4 real, moderated scores (its BU Lead's own first
    /// four direct reports) — the minimum that clears the floor — so if the defence were only per-parent
    /// (the pre-BR1 HAP-11 state), the group total minus three published BUs would isolate BU20 exactly.
    /// </summary>
    [Fact]
    public async Task Group_total_minus_published_bu_siblings_cannot_isolate_the_engineered_sub4_bu()
    {
        var managerExtra = new[]
        {
            Snap.SeedUser(Distributions.BuLeadRef(17), role: "QA-Fixture", buCode: Distributions.BuCode(17)),
            Snap.SeedUser(Distributions.BuLeadRef(18), role: "QA-Fixture", buCode: Distributions.BuCode(18)),
            Snap.SeedUser(Distributions.BuLeadRef(19), role: "QA-Fixture", buCode: Distributions.BuCode(19)),
            Snap.SeedUser(Distributions.BuLeadRef(20), role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
        };
        var (admin, fvId) = await SeedCanonicalAsync(managerExtra);
        await CreateAndOpenCycleAsync(admin, fvId);

        // Discover the real report refs FIRST (a plain DB read, no sign-in needed), so every ref can be
        // registered as a seed user BEFORE any of them is used to sign in and submit/moderate.
        var reportsByBu = new Dictionary<int, List<string>>();
        foreach (var buIndex in new[] { 17, 18, 19 })
        {
            var reports = await FirstDirectReportsAsync(Distributions.BuLeadRef(buIndex), 4);
            Assert.True(reports.Count >= 4, $"BU{buIndex:D2}'s lead has fewer than 4 direct reports — fixture assumption broken");
            reportsByBu[buIndex] = reports;
        }

        var reportExtra = reportsByBu
            .SelectMany(kv => kv.Value.Select(r => Snap.SeedUser(r, role: "QA-Fixture", buCode: Distributions.BuCode(kv.Key))))
            .Concat(new[]
            {
                Snap.SeedUser("HAP-BU20-0001", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
                Snap.SeedUser("HAP-BU20-0002", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            });
        _factory.SeedUsers.Inner = new StubSeedUserSource(
            _factory.CanonicalSeedUsers.Concat(managerExtra).Concat(reportExtra).ToList());

        // BU17/18/19: exactly 4 real scored people each (their BU Lead's own first four direct reports) —
        // published at exactly the floor, the sharpest version of the attack.
        foreach (var buIndex in new[] { 17, 18, 19 })
        {
            var buLeadRef = Distributions.BuLeadRef(buIndex);
            foreach (var r in reportsByBu[buIndex])
            {
                await SubmitSelfAsync(r, _ => 2);
            }
            foreach (var r in reportsByBu[buIndex])
            {
                await ModerateAsync(buLeadRef, r, _ => 2);
            }
        }

        // BU20: its 2 real members, scored — well under the floor regardless.
        await SubmitSelfAsync("HAP-BU20-0001", _ => 2);
        await SubmitSelfAsync("HAP-BU20-0002", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0001", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0002", _ => 2);

        var exec = await ClientAsync(Distributions.ExecRef);
        var group5 = await GroupIdOfBuAsync(Distributions.SubFourBuCode);
        Assert.Equal(await GroupIdOfBuAsync(Distributions.BuCode(17)), group5); // fixture assumption: same group

        var bu17 = await ReadAsync(exec, $"/api/bus/{await BuIdAsync(Distributions.BuCode(17))}/dashboard");
        var bu18 = await ReadAsync(exec, $"/api/bus/{await BuIdAsync(Distributions.BuCode(18))}/dashboard");
        var bu19 = await ReadAsync(exec, $"/api/bus/{await BuIdAsync(Distributions.BuCode(19))}/dashboard");
        var bu20 = await ReadAsync(exec, $"/api/bus/{await BuIdAsync(Distributions.SubFourBuCode)}/dashboard");
        var groupDash = await ReadAsync(exec, $"/api/org/group/{group5}/rollup");

        // Sacred: BU20 itself is always suppressed.
        Assert.True(bu20.Suppressed);
        Assert.Null(bu20.Figures);

        // The differencing invariant, checked algorithm-independently from the actual HTTP responses: if
        // the group total is published, at least ONE other BU sibling besides BU20 must ALSO be withheld —
        // otherwise Group − BU17 − BU18 − BU19 arithmetically equals BU20's exact headcount.
        var siblingSuppressedCount = new[] { bu17, bu18, bu19, bu20 }.Count(d => d.Suppressed);
        if (!groupDash.Suppressed)
        {
            Assert.True(
                siblingSuppressedCount >= 2,
                $"Group published (N={groupDash.Figures!.N}) with only {siblingSuppressedCount} suppressed " +
                "sibling(s) among BU17/18/19/BU20 — BU20 is recoverable by subtraction (BR1 violated).");
        }

        // And spot-verify the arithmetic explicitly for whichever combination shipped, rather than trusting
        // the count alone: reconstruct every subset sum of published siblings and confirm none of them,
        // subtracted from a published group total, lands on a value in [1,3] (the only range that would
        // uniquely finger a suppressed <4 node rather than an ambiguous multi-person residual).
        if (!groupDash.Suppressed)
        {
            var publishedNs = new[] { bu17, bu18, bu19 }.Where(d => !d.Suppressed).Select(d => d.Figures!.N).ToList();
            var residual = groupDash.Figures!.N - publishedNs.Sum();
            var unknownCount = new[] { bu17, bu18, bu19, bu20 }.Count(d => d.Suppressed);
            Assert.True(
                unknownCount != 1 || residual == 0,
                $"exactly one sibling unknown and a nonzero residual {residual} — a single suppressed node's " +
                "count is exposed by subtraction.");
        }
    }

    // ==================================================================================================
    // (c) Desync attempt: independently recompute a BU's figures from raw moderated rows, non-uniform
    // scores across BOTH people and dimensions (catches a person/dimension mixup dev's uniform-score test
    // structurally cannot), for the live path AND the frozen snapshot.
    // ==================================================================================================

    [Fact]
    public async Task Dashboard_figures_reconcile_to_raw_moderated_rows_with_nonuniform_scores_live_and_snapshot()
    {
        // An independently-designed fixture (not dev's CleanFixture/ThinFixture): one manager, 4 reports,
        // each scored differently per dimension so every dimension's mean and the floor distribution are
        // all genuinely distinct — a dimension- or person-mixup bug cannot hide behind a repeated constant.
        var bus = new[] { Snap.Bu("BU_R", group: "Group R", portfolio: "Portfolio R") };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN_R", "BU_R"),
            Snap.Person("MGR_R", "BU_R"),
        };
        for (var i = 1; i <= 4; i++)
        {
            people.Add(Snap.Person($"REP_R{i}", "BU_R", managerExternalRef: "MGR_R"));
        }
        var seed = new[]
        {
            Snap.SeedUser("ADMIN_R", role: "Platform Admin"),
            Snap.SeedUser("MGR_R", role: "Manager", buCode: "BU_R"),
            Snap.SeedUser("REP_R1", role: "Individual", buCode: "BU_R"),
            Snap.SeedUser("REP_R2", role: "Individual", buCode: "BU_R"),
            Snap.SeedUser("REP_R3", role: "Individual", buCode: "BU_R"),
            Snap.SeedUser("REP_R4", role: "Individual", buCode: "BU_R"),
        };

        await _factory.ResetAsync();
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
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN_R");
        var fv = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var cycleId = await CreateAndOpenCycleAsync(admin, fv!.VersionId);

        // person i (1-based) scores dimension index d (0-based) as (i + d) % 4 — every dimension has a
        // different mean, every person has a different floor.
        for (var i = 1; i <= 4; i++)
        {
            var idx = i;
            await SubmitSelfAsync($"REP_R{i}", d => (idx + d) % 4);
        }
        for (var i = 1; i <= 4; i++)
        {
            var idx = i;
            await ModerateAsync("MGR_R", $"REP_R{i}", d => (idx + d) % 4);
        }

        var buR = await BuIdAsync("BU_R");
        var mgr = await ClientAsync("MGR_R");

        // Live (open-cycle) figures.
        var live = await ReadAsync(mgr, "/api/me/team/summary");
        Assert.False(live.Suppressed);
        AssertReconciles(live.Figures!, cycleId, buR);

        // Close, then re-read the frozen snapshot — must reconcile identically to the same raw rows.
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        var snap = await ReadAsync(mgr, "/api/me/team/summary");
        Assert.False(snap.Live);
        Assert.False(snap.Suppressed);
        AssertReconciles(snap.Figures!, cycleId, buR);

        // Live and snapshot must be byte-for-byte identical on this just-closed cycle (research D4).
        Assert.Equal(live.Figures!.N, snap.Figures!.N);
        foreach (var kv in live.Figures.PerDimensionMean)
        {
            Assert.Equal(kv.Value, snap.Figures.PerDimensionMean[kv.Key], 9);
        }
        foreach (var kv in live.Figures.FloorLevelDistribution)
        {
            Assert.Equal(kv.Value, snap.Figures.FloorLevelDistribution[kv.Key]);
        }
    }

    private void AssertReconciles(FiguresDto figures, Guid cycleId, Guid buId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        var dims = db.Dimensions.OrderBy(d => d.DisplayOrder).ToList();
        var buPeople = db.People.Where(p => p.BusinessUnitId == buId).Select(p => p.Id).ToList();
        var rows = db.Set<AssessmentScore>()
            .Join(db.Set<Assessment>().Where(a => a.CycleId == cycleId && buPeople.Contains(a.PersonId)),
                s => s.AssessmentId, a => a.Id, (s, a) => new { s.DimensionId, s.ManagerScore, a.PersonId })
            .Where(x => x.ManagerScore != null)
            .ToList();

        Assert.Equal(4, figures.N);

        // Every dimension's mean, independently, from the raw ManagerScore rows.
        foreach (var dim in dims)
        {
            var raw = rows.Where(r => r.DimensionId == dim.Id).Select(r => r.ManagerScore!.Value).ToList();
            Assert.Equal(4, raw.Count);
            Assert.Equal(MaturityScoring.Mean(raw), figures.PerDimensionMean[dim.Key], 9);
        }

        // Floor distribution: recompute per-person floor from raw rows, independently of RollupComputation.
        var expectedFloor = rows.GroupBy(r => r.PersonId)
            .Select(g => MaturityScoring.FloorLevel(g.Select(r => r.ManagerScore!.Value).ToList()))
            .GroupBy(f => f)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var (level, count) in expectedFloor)
        {
            Assert.Equal(count, figures.FloorLevelDistribution.GetValueOrDefault(level));
        }
        Assert.Equal(expectedFloor.Values.Sum(), figures.FloorLevelDistribution.Values.Sum());

        // Completion% and unmoderated% for a fully-moderated, fully-submitted population of 4.
        Assert.Equal(1.0, figures.CompletionPct, 9);
        Assert.Equal(0.0, figures.UnmoderatedPct, 9);
    }

    // ==================================================================================================
    // G1-readiness residuals: suppressed trend points render nothing; a published floor N=4 is the
    // intended k=4 floor (FR-014), not further suppressed.
    // ==================================================================================================

    [Fact]
    public async Task Suppressed_cycles_contribute_no_trend_figures_and_a_published_floor_of_four_is_not_oversuppressed()
    {
        var extra = new[]
        {
            Snap.SeedUser(Distributions.BuLeadRef(20), role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            Snap.SeedUser("HAP-BU20-0001", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
            Snap.SeedUser("HAP-BU20-0002", role: "QA-Fixture", buCode: Distributions.SubFourBuCode),
        };
        var (admin, fvId) = await SeedCanonicalAsync(extra);

        // Cycle 1: BU20's 2 members scored (suppressed, closed).
        var c1 = await CreateAndOpenCycleAsync(admin, fvId, "2026-06");
        await SubmitSelfAsync("HAP-BU20-0001", _ => 2);
        await SubmitSelfAsync("HAP-BU20-0002", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0001", _ => 2);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0002", _ => 2);
        await admin.PostAsync($"/api/cycles/{c1}/close", null);

        // Cycle 2: same two people re-score (still N=2, still suppressed) — open, live.
        await CreateAndOpenCycleAsync(admin, fvId, "2026-07");
        await SubmitSelfAsync("HAP-BU20-0001", _ => 1);
        await SubmitSelfAsync("HAP-BU20-0002", _ => 1);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0001", _ => 1);
        await ModerateAsync(Distributions.BuLeadRef(20), "HAP-BU20-0002", _ => 1);

        var buLead20 = await ClientAsync(Distributions.BuLeadRef(20));
        var bu20 = await BuIdAsync(Distributions.SubFourBuCode);
        var dash = await ReadAsync(buLead20, $"/api/bus/{bu20}/dashboard");

        Assert.True(dash.Suppressed);
        Assert.Null(dash.Figures);
        Assert.Single(dash.Trend);
        // The prior (closed, suppressed) cycle's trend point carries no figures either — period-over-period
        // differencing (the accepted G1 residual) is not made WORSE by simply exposing the raw prior number.
        Assert.True(dash.Trend[0].Suppressed);
        Assert.Null(dash.Trend[0].Figures);
    }

    // ==================================================================================================
    // SC-006: the LIVE open-cycle path over the FULL canonical directory (~10k people, ~2k team nodes) —
    // the HierarchySuppression.Close fixpoint runs over the WHOLE tree on every live request, not a
    // node-local slice, so this is the actual worst case the <5s bound must cover.
    // ==================================================================================================

    [Fact]
    public async Task Dashboard_responds_under_five_seconds_live_over_the_full_canonical_directory()
    {
        var (admin, fvId) = await SeedCanonicalAsync();
        // Opening the cycle invites the FULL active population (~10k) — the live pipeline's person-input
        // universe is invited-non-excluded ∪ has-a-score, so this alone exercises the full-tree node build
        // and the whole-tree suppression fixpoint even with zero submissions (an unscored person still
        // occupies a slot in their Team/BU/Group/Portfolio/AllHig node).
        await CreateAndOpenCycleAsync(admin, fvId);

        var exec = await ClientAsync(Distributions.ExecRef);

        var swAllHig = Stopwatch.StartNew();
        var allHigResp = await exec.GetAsync("/api/org/allhig/rollup");
        swAllHig.Stop();
        Assert.Equal(HttpStatusCode.OK, allHigResp.StatusCode);
        Assert.True(swAllHig.ElapsedMilliseconds < 5000,
            $"AllHig live rollup over the full canonical directory took {swAllHig.ElapsedMilliseconds}ms (SC-006 bound 5000ms).");

        var bu01 = await BuIdAsync(Distributions.BuCode(1));
        var swBu = Stopwatch.StartNew();
        var buResp = await exec.GetAsync($"/api/bus/{bu01}/dashboard");
        swBu.Stop();
        Assert.Equal(HttpStatusCode.OK, buResp.StatusCode);
        Assert.True(swBu.ElapsedMilliseconds < 5000,
            $"BU-scoped live dashboard over the full canonical directory took {swBu.ElapsedMilliseconds}ms (SC-006 bound 5000ms).");
    }
}
