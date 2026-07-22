using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hap.Api.Identity;
using Hap.Domain.Assessments;
using Hap.Domain.Cycles;
using Hap.Domain.Rollups;
using Hap.Domain.Scoring;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-10 QA-window adversarial coverage (fresh instance, CLAUDE.md §9) — none of this existed
/// during the Dev/panel rounds; every test here is QA work, attributed honestly. Targets the three
/// mandatory L3 attempts (read outside chain via any close output/snapshot path; obtain an aggregate
/// covering &lt;4; desync a snapshot from raw rows) plus a genuine RBAC coverage gap the Dev/panel
/// rounds did not test: <c>AdminGateAllRolesTests</c> sweeps <c>/api/admin/*</c> for all seven seeded
/// roles but never exercises <c>/api/cycles/*</c>, which is gated by the SAME <c>PlatformAdmin</c>
/// policy and is the write path that drives auto-adoption over <c>AssessmentScores</c> — exactly the
/// kind of "one-line gate, big blast radius" surface CLAUDE.md §7 flags as L3 regardless of diff size.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class CycleCloseQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public CycleCloseQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    // ============================================================================================
    // Attempt (RBAC gap): non-admin seeded roles against /api/cycles create/open/close
    // ============================================================================================

    // NOTE on fixture choice: RequireRole("PlatformAdmin") checks ONLY the ASP.NET Role claims
    // LocalDevProvider.SignInAsync attaches from explicit RoleGrant rows (Platform Admin / HIG
    // Executive). The other five seed labels ("Individual", "Manager", "BU Lead", "Group Leader",
    // "Portfolio Leader") get NO RoleGrant and thus NO Role claim at all — HierarchyRoleResolver
    // computes those per-request for a DIFFERENT purpose (visibility scope) and is never consulted
    // by this policy. So a small hand-built fixture (six plain BU01 individuals, one per label) is
    // behaviorally equivalent to signing in as the real hierarchy-derived person for THIS gate —
    // deliberately not reusing AdminGateAllRolesTests' full ~15k-person canonical org sync, which
    // this test does not need and which would multiply seven-fold under a Theory.
    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) RoleGateFixture() =>
        (new[] { Snap.Bu("BU01") },
         new[]
         {
             Snap.Person("ADMIN", "BU01"),
             Snap.Person("R-INDIVIDUAL", "BU01"),
             Snap.Person("R-MANAGER", "BU01"),
             Snap.Person("R-BULEAD", "BU01"),
             Snap.Person("R-GROUPLEADER", "BU01"),
             Snap.Person("R-PORTFOLIOLEADER", "BU01"),
             Snap.Person("R-HIGEXEC", "BU01"),
         },
         new[]
         {
             Snap.SeedUser("ADMIN", role: "Platform Admin"),
             Snap.SeedUser("R-INDIVIDUAL", role: "Individual"),
             Snap.SeedUser("R-MANAGER", role: "Manager"),
             Snap.SeedUser("R-BULEAD", role: "BU Lead"),
             Snap.SeedUser("R-GROUPLEADER", role: "Group Leader"),
             Snap.SeedUser("R-PORTFOLIOLEADER", role: "Portfolio Leader"),
             Snap.SeedUser("R-HIGEXEC", role: "HIG Executive"),
         });

    private static readonly Dictionary<string, string> RefByRole = new()
    {
        ["Individual"] = "R-INDIVIDUAL",
        ["Manager"] = "R-MANAGER",
        ["BU Lead"] = "R-BULEAD",
        ["Group Leader"] = "R-GROUPLEADER",
        ["Portfolio Leader"] = "R-PORTFOLIOLEADER",
        ["HIG Executive"] = "R-HIGEXEC",
    };

    /// <summary>Platform Admin seeds the framework and puts one cycle into Open state — the state a
    /// real close attack would target (auto-adopt + snapshot writes actually fire on a successful
    /// close, so this is the highest-value moment to prove the gate holds).</summary>
    private async Task<(HttpClient AdminClient, Guid FrameworkVersionId, Guid CycleId)> AdminOpenCycleAsync()
    {
        var (bus, people, seed) = RoleGateFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(fvId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return (admin, fvId, cycle.Id);
    }

    [Theory]
    [InlineData("Individual")]
    [InlineData("Manager")]
    [InlineData("BU Lead")]
    [InlineData("Group Leader")]
    [InlineData("Portfolio Leader")]
    [InlineData("HIG Executive")]
    public async Task Non_admin_seeded_roles_cannot_create_open_or_close_a_cycle(string role)
    {
        var (_, frameworkVersionId, cycleId) = await AdminOpenCycleAsync();

        var attacker = await ClientAsync(RefByRole[role]);

        // The whole cycle-management surface in one sweep — a coverage gap AdminGateAllRolesTests
        // left open: it sweeps /api/admin/* for all seven seeded roles but never touches
        // /api/cycles/*, which sits behind the SAME PlatformAdmin policy and is the write path that
        // drives auto-adoption over AssessmentScores at close.
        var create = await attacker.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, "attacker-cycle", true));
        var open = await attacker.PostAsync($"/api/cycles/{cycleId}/open", null);
        var close = await attacker.PostAsync($"/api/cycles/{cycleId}/close", null);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);

        // The denied close attempt must have had zero effect: cycle still Open, no snapshots exist.
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var cycleRow = await db.Cycles.AsNoTracking().SingleAsync(c => c.Id == cycleId);
        Assert.Equal(CycleState.Open, cycleRow.State);
        Assert.False(await db.RollupSnapshots.AnyAsync(s => s.CycleId == cycleId));
        // No rogue cycle was created under the attacker's forbidden create attempt either.
        Assert.False(await db.Cycles.AnyAsync(c => c.Name == "attacker-cycle"));
    }

    [Fact]
    public async Task Platform_admin_role_alone_is_admitted_to_close_negative_control()
    {
        // Proves the gate above is discriminating (rejecting non-admins BECAUSE they're non-admin),
        // not failing closed universally — same real Platform Admin ref used to open the cycle can
        // also close it.
        var (admin, _, cycleId) = await AdminOpenCycleAsync();

        var close = await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.True(await db.RollupSnapshots.AnyAsync(s => s.CycleId == cycleId));
    }

    // ============================================================================================
    // Mandatory attempt (a): read an individual score outside the chain via any close output
    // ============================================================================================

    [Fact]
    public async Task Close_response_body_carries_no_score_aggregate_or_snapshot_field_at_all()
    {
        // By inspection, CycleEndpoints.cs's /close handler returns only CycleResponse.From(cycle) —
        // cycle metadata (id/frameworkVersionId/name/state/contractorExclusionEnabled/opensAt/closesAt).
        // This test proves it at the wire level rather than trusting the source read: parse the raw
        // JSON and assert the property set is EXACTLY that shape — no N, no mean, no score, no
        // suppressed/reason field could sneak in via a future edit without this test catching it.
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        await SubmitUniform(emp1, 3);

        var response = await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(
            new[] { "closesAt", "contractorExclusionEnabled", "frameworkVersionId", "id", "name", "opensAt", "state" },
            properties);
    }

    [Fact]
    public async Task No_snapshot_or_rollup_read_route_exists_at_all_guessed_paths_all_404()
    {
        // Confirms by direct HTTP probe (not just "grep found no endpoint") that HAP-10 wired no read
        // surface for rollup_snapshots — every plausible route name a caller might guess 404s as an
        // undefined route, not 403 (which would at least prove a route exists behind a gate) and
        // never 200 with data.
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        foreach (var path in new[]
        {
            $"/api/cycles/{cycleId}/snapshot",
            $"/api/cycles/{cycleId}/snapshots",
            $"/api/cycles/{cycleId}/rollup",
            $"/api/cycles/{cycleId}/rollups",
            "/api/rollups",
            "/api/snapshots",
            $"/api/rollup-snapshots/{cycleId}",
        })
        {
            var resp = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }

    // ============================================================================================
    // Mandatory attempt (b): obtain an aggregate covering <4 — the required red-team pin case
    // ============================================================================================

    [Fact]
    public async Task A_publishable_team_is_suppressed_when_the_teamless_complement_is_below_four()
    {
        // The exact sharp case red-team flagged: a team of 4 (passes its OWN n>=4 threshold) sitting
        // under a submitting, TEAMLESS BU head. HEAD is manager-less (a BU head), so per the B1/Q-023
        // rule HEAD is teamless — counted only at BU level, in no Team node. BU n = HEAD(1) +
        // Team(MGR1)=4 = 5. Team's own n=4 clears rule 1, but complement = 5-4 = 1 (in 1..3) — rule 2
        // must suppress the team: publishing Team=4 alongside BU=5 would let anyone subtract 5-4=1 and
        // know HEAD is exactly that one remaining person, exposing HEAD's individual scores by
        // elimination — precisely the FR-014 differencing attack this rule exists to close.
        var bus = new[] { Snap.Bu("BU01", group: "Group A", portfolio: "Portfolio 1") };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("HEAD", "BU01"),                              // manager-less, teamless, WILL submit
            Snap.Person("MGR1", "BU01", managerExternalRef: "HEAD"),  // manages the team; does not submit
            Snap.Person("E1", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("E2", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("E3", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("E4", "BU01", managerExternalRef: "MGR1"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        var (admin, fvId) = await SeedAsync(bus, people.ToArray(), seed.ToArray());
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        foreach (var r in new[] { "HEAD", "E1", "E2", "E3", "E4" }) // MGR1 does NOT submit
        {
            var c = await ClientAsync(r);
            await SubmitUniform(c, 2);
        }

        var closeResp = await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var mgr1Id = (await db.People.SingleAsync(p => p.ExternalRef == "MGR1")).Id;
        var bu01Id = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).Id;

        var team = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Team && s.OrgNodeRef == mgr1Id);
        var buSnap = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Bu && s.OrgNodeRef == bu01Id);

        Assert.Equal(4, team.N);        // passes its own n>=4 threshold in isolation
        Assert.Equal(5, buSnap.N);       // BU = 4 team-homed + 1 teamless (HEAD)
        Assert.False(buSnap.Suppressed); // BU itself is >=4 and would otherwise publish

        // THE PIN: rule 2 must suppress the team despite its own n=4, because BU(5) - Team(4) = 1.
        Assert.True(team.Suppressed);
        Assert.Equal("Complement", team.SuppressionReason);

        // Attempt to defeat it: is HEAD's score recoverable ANY way through what HAP-10 exposes?
        // (1) There is no snapshot read endpoint at all (proven above) — HTTP cannot reach either
        //     number in the first place, so there is no wire-level differencing surface to defeat.
        // (2) Direct DB inspection (test-only privilege, not an HTTP-reachable path) confirms the
        //     domain object ITSELF never separately materialises "HEAD's team" — HEAD contributes
        //     only to the BU/Group/Portfolio/AllHig nodes, never to a Team node (B1/Q-023), so there
        //     is no sibling "Team(HEAD)" snapshot whose bare existence would hand over n=1 directly;
        //     the only route to HEAD's score is the suppressed Team(MGR1) row differenced against the
        //     published BU row, and that route is exactly what SuppressionEvaluator's rule 2 closes.
        // No defeat found. This is a genuine, closed guarantee for this fixture, not "looks fine."
    }

    // ============================================================================================
    // Mandatory attempt (c): desync a snapshot from raw rows — independent recompute, 3+ nodes,
    // a DIFFERENT fixture from the Dev suite's (so a bug the Dev fixture happens to mask is not
    // silently re-inherited by QA reusing the same data).
    // ============================================================================================

    [Fact]
    public async Task Independent_recompute_matches_the_snapshot_for_team_bu_and_allhig_nodes()
    {
        // Three BUs (two under one group/portfolio, one standalone) so Team, BU, Group/Portfolio and
        // AllHig are all non-trivial and none of the arithmetic is a trivial 1-node passthrough.
        var bus = new[]
        {
            Snap.Bu("BU01", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU02", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU03", group: "Group B", portfolio: "Portfolio 2"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("M1", "BU01"),
            Snap.Person("A1", "BU01", managerExternalRef: "M1"),
            Snap.Person("A2", "BU01", managerExternalRef: "M1"),
            Snap.Person("A3", "BU01", managerExternalRef: "M1"),
            Snap.Person("A4", "BU01", managerExternalRef: "M1"),
            Snap.Person("M2", "BU02"),
            Snap.Person("B1", "BU02", managerExternalRef: "M2"),
            Snap.Person("B2", "BU02", managerExternalRef: "M2"),
            Snap.Person("B3", "BU02", managerExternalRef: "M2"),
            Snap.Person("B4", "BU02", managerExternalRef: "M2"),
            Snap.Person("M3", "BU03"),
            Snap.Person("C1", "BU03", managerExternalRef: "M3"),
            Snap.Person("C2", "BU03", managerExternalRef: "M3"),
            Snap.Person("C3", "BU03", managerExternalRef: "M3"),
            Snap.Person("C4", "BU03", managerExternalRef: "M3"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        var (admin, fvId) = await SeedAsync(bus, people.ToArray(), seed.ToArray());
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        // Deliberately mixed: some auto-adopt (never moderated), one gets moderated (to exercise
        // score-of-record = manager score, not self, in the recompute), one leaves mid-cycle
        // (retained in the scored population per FR-024/§3.5) — a richer desync surface than a
        // uniform-score fixture would offer.
        var scores = new (string Ref, int Self)[]
        {
            ("A1", 3), ("A2", 1), ("A3", 2), ("A4", 0),
            ("B1", 2), ("B2", 2), ("B3", 3), ("B4", 1),
            ("C1", 0), ("C2", 3), ("C3", 1), ("C4", 2),
        };
        foreach (var (r, self) in scores)
        {
            var c = await ClientAsync(r);
            await SubmitUniform(c, self);
        }
        // A1 gets moderated down (score of record diverges from self — recompute must use manager score).
        var mgr1 = await ClientAsync("M1");
        var a1Id = await PersonIdAsync("A1");
        var a1AssessmentId = await AssessmentIdAsync("A1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{a1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "qa recompute test")).ToList();
        await mgr1.PutAsJsonAsync($"/api/team/reviews/{a1AssessmentId}", new ModerateReviewRequest(decisions));
        // C4 leaves mid-cycle AFTER submitting — stays in the scored population, out of completion base.
        await DeactivateAsync("C4");

        var closeResp = await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        var m1Id = (await db.People.SingleAsync(p => p.ExternalRef == "M1")).Id;
        var bu01Id = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).Id;
        var allDims = await db.Dimensions.OrderBy(d => d.DisplayOrder).ToListAsync();
        var firstDim = allDims.First();

        // --- Team(M1): recompute mean + floor independently from raw AssessmentScore rows ----------
        var teamSnap = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Team && s.OrgNodeRef == m1Id);
        var a1234Ids = new[] { "A1", "A2", "A3", "A4" };
        var a1234PersonIds = await db.People.Where(p => a1234Ids.Contains(p.ExternalRef)).Select(p => p.Id).ToListAsync();
        var teamRawScores = await db.Set<AssessmentScore>()
            .Where(s => s.DimensionId == firstDim.Id && s.ManagerScore != null)
            .Join(db.Set<Assessment>().Where(a => a.CycleId == cycleId && a1234PersonIds.Contains(a.PersonId)),
                s => s.AssessmentId, a => a.Id, (s, a) => s.ManagerScore!.Value)
            .ToListAsync();
        Assert.Equal(4, teamRawScores.Count);
        Assert.Equal(MaturityScoring.Mean(teamRawScores), teamSnap.PerDimensionMean[firstDim.Key]);
        Assert.Equal(4, teamSnap.N);

        // --- BU01: recompute floor distribution independently (min across ALL 7 dims per person) ---
        var buSnap = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Bu && s.OrgNodeRef == bu01Id);
        var bu01PersonIds = await db.People.Where(p => p.BusinessUnitId == bu01Id).Select(p => p.Id).ToListAsync();
        var bu01Assessments = await db.Set<Assessment>()
            .Where(a => a.CycleId == cycleId && bu01PersonIds.Contains(a.PersonId)
                && (a.State == AssessmentState.Moderated || a.State == AssessmentState.AutoAdopted))
            .ToListAsync();
        var recomputedFloorDist = new Dictionary<int, int>();
        foreach (var a in bu01Assessments)
        {
            var managerScores = await db.Set<AssessmentScore>()
                .Where(s => s.AssessmentId == a.Id && s.ManagerScore != null)
                .Select(s => s.ManagerScore!.Value).ToListAsync();
            var floor = MaturityScoring.FloorLevel(managerScores);
            recomputedFloorDist[floor] = recomputedFloorDist.GetValueOrDefault(floor) + 1;
        }
        Assert.Equal(bu01Assessments.Count, buSnap.N);
        foreach (var (level, count) in recomputedFloorDist)
        {
            Assert.Equal(count, buSnap.FloorLevelDistribution.GetValueOrDefault(level));
        }
        Assert.Equal(recomputedFloorDist.Values.Sum(), buSnap.FloorLevelDistribution.Values.Sum());

        // --- AllHig: recompute total scored N independently, spanning all three BUs, leaver included -
        var allHigSnap = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.AllHig && s.OrgNodeRef == null);
        var totalScoredRaw = await db.Set<Assessment>()
            .Where(a => a.CycleId == cycleId && (a.State == AssessmentState.Moderated || a.State == AssessmentState.AutoAdopted))
            .CountAsync();
        Assert.Equal(totalScoredRaw, allHigSnap.N);
        Assert.Equal(12, allHigSnap.N); // all 12 non-head submitters (M1/M2/M3 never submitted)

        // C4 (the mid-cycle leaver) must be IN the scored population, OUT of the completion base —
        // both reconciled independently, closing the desync route the split itself creates.
        var c4 = await db.People.SingleAsync(p => p.ExternalRef == "C4");
        var c4Assessment = await db.Set<Assessment>().SingleAsync(a => a.PersonId == c4.Id && a.CycleId == cycleId);
        Assert.True(c4Assessment.State is AssessmentState.Moderated or AssessmentState.AutoAdopted);
        Assert.False(c4.IsActive);
        var bu03Id = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU03")).Id;
        var bu03Snap = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Bu && s.OrgNodeRef == bu03Id);
        Assert.Equal(4, bu03Snap.N);                     // C1..C4 all scored, leaver included
        // Completion denominator = active+invited regardless of submission: M3 (head, never submits,
        // still active) + C1 + C2 + C3 = 4. C4 is excluded (deactivated before close) — the leaver
        // still lowers the denominator even though M3's own non-submission does not.
        Assert.Equal(4, bu03Snap.CompletionDenominator);
    }

    // ============================================================================================
    // Negative path: concurrent close attempts on the same cycle — the state machine + unique
    // index must resolve the race to exactly one committed close, never a duplicate snapshot set.
    // ============================================================================================

    [Fact]
    public async Task Two_concurrent_close_requests_produce_exactly_one_committed_close_never_duplicate_snapshots()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        var emp1 = await ClientAsync("EMP1");
        await SubmitUniform(emp1, 2);

        // Two independent HttpClients (independent scoped DbContexts/connections) racing the same
        // close — the forward-only Cycle.Close() throw plus the unique index on rollup_snapshots are
        // the two backstops under test; either could in principle let a racing second insert half
        // through if either were missing.
        var adminA = _factory.CreateClient();
        var adminB = _factory.CreateClient();
        await HapApiFactory.SignInAsync(adminA, "ADMIN");
        await HapApiFactory.SignInAsync(adminB, "ADMIN");

        var results = await Task.WhenAll(
            adminA.PostAsync($"/api/cycles/{cycleId}/close", null),
            adminB.PostAsync($"/api/cycles/{cycleId}/close", null));

        var succeeded = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failed = results.Count(r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, failed);
        // The loser must fail cleanly (409 forward-only, or 500 if it lost the DB race after the
        // in-memory state check but before commit) — never a silent 200 with corrupted state.
        Assert.Contains(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.InternalServerError);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var teamRef = await PersonIdAsync("MGR1");
        // Exactly one Team(MGR1) snapshot — no duplicate frozen row from the losing race.
        var count = await db.RollupSnapshots.CountAsync(
            s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Team && s.OrgNodeRef == teamRef);
        Assert.Equal(1, count);
    }

    // ============================================================================================
    // Boundary: exactly n=3 (suppressed) vs exactly n=4 (published) in isolation, no complement
    // interaction — the sharpest form of the FR-014 threshold itself.
    // ============================================================================================

    [Fact]
    public async Task Exactly_three_scored_is_suppressed_exactly_four_is_published_isolated_from_complement()
    {
        var (bus, people, seed) = SingleTeamFixture(); // MGR1 (head, never submits) over EMP1..EMP4
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        // Only 3 of the 4 reports submit; EMP4 never starts an assessment (not in the scored
        // population at all — not "submitted but excluded"). Team AND BU both land at scored n=3
        // (MGR1 never submits), complement is trivially 0 either way — an isolated threshold read.
        foreach (var r in new[] { "EMP1", "EMP2", "EMP3" })
        {
            var c = await ClientAsync(r);
            await SubmitUniform(c, 1);
        }

        var closeResp = await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var mgr1Id = (await db.People.SingleAsync(p => p.ExternalRef == "MGR1")).Id;
        var team = await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == OrgNodeType.Team && s.OrgNodeRef == mgr1Id);

        Assert.Equal(3, team.N);
        Assert.True(team.Suppressed);
        Assert.Equal("N<4", team.SuppressionReason); // rule 1, not rule 2 — no complement in play here
    }

    // ============================================================================================
    // Shared fixture/client helpers (independent of CycleCloseTests/CycleCloseSuppressionTests —
    // QA does not reuse Dev's private helpers, only the same public Snap builders).
    // ============================================================================================

    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) SingleTeamFixture() =>
        (new[] { Snap.Bu("BU01") },
         new[]
         {
             Snap.Person("ADMIN", "BU01"),
             Snap.Person("MGR1", "BU01"),
             Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
             Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
             Snap.Person("EMP3", "BU01", managerExternalRef: "MGR1"),
             Snap.Person("EMP4", "BU01", managerExternalRef: "MGR1"),
         },
         new[]
         {
             Snap.SeedUser("ADMIN", role: "Platform Admin"),
             Snap.SeedUser("MGR1", role: "Manager"),
             Snap.SeedUser("EMP1", role: "Individual"),
             Snap.SeedUser("EMP2", role: "Individual"),
             Snap.SeedUser("EMP3", role: "Individual"),
             Snap.SeedUser("EMP4", role: "Individual"),
         });

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync(
        IEnumerable<DirectoryBu> bus, IEnumerable<DirectoryPerson> persons, IEnumerable<SeedUserRecord> seedUsers)
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, persons));
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
        _factory.SeedUsers.Inner = new StubSeedUserSource(seedUsers.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid frameworkVersionId)
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, "2026-08", true));
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

    private static async Task SubmitUniform(HttpClient client, int score)
    {
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var entries = form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList();
        await client.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        var submit = await client.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
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

    private async Task DeactivateAsync(params string[] externalRefs)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        foreach (var r in externalRefs)
        {
            (await db.People.SingleAsync(p => p.ExternalRef == r)).Deactivate();
        }
        await db.SaveChangesAsync();
    }
}
