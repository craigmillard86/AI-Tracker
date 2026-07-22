using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Assessments;
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
/// HAP-10 L3 guarantees at close: frozen N&lt;4 + complement suppression per node (FR-014/071, research
/// D2), snapshot reconciliation across the hierarchy (the desync guard, Art. VI.4), and FR-070 departure
/// escalation interplay with auto-adoption. Multi-BU/group/portfolio fixture so the suppression tree and
/// the roll-up sums are non-trivial. Every test is <c>Category=PrivacyReporting</c>.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class CycleCloseSuppressionTests
{
    private readonly HapApiFactory _factory;

    public CycleCloseSuppressionTests(HapApiFactory factory) => _factory = factory;

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
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, managerScore, "close-test")).ToList();
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

    private async Task<RollupSnapshot> SnapshotAsync(Guid cycleId, OrgNodeType type, Guid? nodeRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == type && s.OrgNodeRef == nodeRef);
    }

    // Two BUs in one group/portfolio. BU01: MGR1 (head, no submit) over EMP1..4 (+ MGR2 who submits),
    // MGR2 over EMP5,EMP6. BU02: MGRB (head, no submit) over EB1..4.
    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) TreeFixture()
    {
        var bus = new[]
        {
            Snap.Bu("BU01", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU02", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("MGR1", "BU01"),
            Snap.Person("MGR2", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("EMP3", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("EMP4", "BU01", managerExternalRef: "MGR1"),
            Snap.Person("EMP5", "BU01", managerExternalRef: "MGR2"),
            Snap.Person("EMP6", "BU01", managerExternalRef: "MGR2"),
            Snap.Person("MGRB", "BU02"),
            Snap.Person("EB1", "BU02", managerExternalRef: "MGRB"),
            Snap.Person("EB2", "BU02", managerExternalRef: "MGRB"),
            Snap.Person("EB3", "BU02", managerExternalRef: "MGRB"),
            Snap.Person("EB4", "BU02", managerExternalRef: "MGRB"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));
        return (bus, people.ToArray(), seed.ToArray());
    }

    private async Task SubmitTreeAsync()
    {
        // Everyone except the two BU heads (MGR1, MGRB) submits, uniform score 2.
        foreach (var r in new[] { "MGR2", "EMP1", "EMP2", "EMP3", "EMP4", "EMP5", "EMP6", "EB1", "EB2", "EB3", "EB4" })
        {
            await SubmitUniformAsync(r, 2);
        }
    }

    [Fact]
    public async Task Suppression_verdicts_are_frozen_per_node_over_the_tree()
    {
        var (bus, people, seed) = TreeFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitTreeAsync();
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        var teamMgr1 = await SnapshotAsync(cycleId, OrgNodeType.Team, await PersonIdAsync("MGR1"));
        var teamMgr2 = await SnapshotAsync(cycleId, OrgNodeType.Team, await PersonIdAsync("MGR2"));
        var teamMgrB = await SnapshotAsync(cycleId, OrgNodeType.Team, await PersonIdAsync("MGRB"));

        // Team(MGR2) = {EMP5,EMP6} = 2 → N<4. Team(MGR1) = {EMP1..4, MGR2} = 5, but BU01(7) − 5 = 2 would
        // expose the 2-person team, so complement suppression also hides MGR1 (research D2, set-based).
        Assert.Equal(2, teamMgr2.N);
        Assert.True(teamMgr2.Suppressed);
        Assert.Equal("N<4", teamMgr2.SuppressionReason);

        Assert.Equal(5, teamMgr1.N);
        Assert.True(teamMgr1.Suppressed);
        Assert.Equal("Complement", teamMgr1.SuppressionReason);

        // Team(MGRB) = {EB1..4} = 4 and BU02(4) − 4 = 0 → published.
        Assert.Equal(4, teamMgrB.N);
        Assert.False(teamMgrB.Suppressed);

        // Every aggregate above team level is ≥4 with a zero complement → published.
        foreach (var type in new[] { OrgNodeType.Bu, OrgNodeType.Group, OrgNodeType.Portfolio, OrgNodeType.AllHig })
        {
            foreach (var snap in await SnapshotsOfTypeAsync(cycleId, type))
            {
                Assert.False(snap.Suppressed, $"{type} {snap.OrgNodeRef} should be published (N={snap.N})");
            }
        }
    }

    private async Task<List<RollupSnapshot>> SnapshotsOfTypeAsync(Guid cycleId, OrgNodeType type)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.RollupSnapshots.AsNoTracking()
            .Where(s => s.CycleId == cycleId && s.OrgNodeType == type).ToListAsync();
    }

    [Fact]
    public async Task Snapshot_totals_reconcile_and_recompute_from_raw_matches_both_populations()
    {
        var (bus, people, seed) = TreeFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitTreeAsync();
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        Guid bu01, bu02, groupId, portfolioId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            bu01 = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).Id;
            bu02 = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU02")).Id;
            groupId = (await db.Groups.SingleAsync()).Id;
            portfolioId = (await db.Portfolios.SingleAsync()).Id;
        }

        var teams = await SnapshotsOfTypeAsync(cycleId, OrgNodeType.Team);
        var bu01Snap = await SnapshotAsync(cycleId, OrgNodeType.Bu, bu01);
        var bu02Snap = await SnapshotAsync(cycleId, OrgNodeType.Bu, bu02);
        var groupSnap = await SnapshotAsync(cycleId, OrgNodeType.Group, groupId);
        var portfolioSnap = await SnapshotAsync(cycleId, OrgNodeType.Portfolio, portfolioId);
        var allHig = await SnapshotAsync(cycleId, OrgNodeType.AllHig, null);

        // Team ids in BU01 are MGR1, MGR2; in BU02, MGRB.
        var mgr1 = await PersonIdAsync("MGR1");
        var mgr2 = await PersonIdAsync("MGR2");
        var mgrB = await PersonIdAsync("MGRB");
        var bu01TeamSum = teams.Where(t => t.OrgNodeRef == mgr1 || t.OrgNodeRef == mgr2).Sum(t => t.N);
        var bu02TeamSum = teams.Where(t => t.OrgNodeRef == mgrB).Sum(t => t.N);

        // Scored-population reconciliation: Σ team n = BU n; Σ BU n = group n = portfolio n = all-HIG n.
        Assert.Equal(bu01Snap.N, bu01TeamSum);                 // 5 + 2 = 7
        Assert.Equal(bu02Snap.N, bu02TeamSum);                 // 4
        Assert.Equal(bu01Snap.N + bu02Snap.N, groupSnap.N);    // 11
        Assert.Equal(groupSnap.N, portfolioSnap.N);
        Assert.Equal(portfolioSnap.N, allHig.N);
        Assert.Equal(11, allHig.N);

        // Completion-base reconciliation (the separate population). BUs partition the org, so the base
        // reconciles cleanly at BU→group→portfolio→all-HIG. It does NOT reconcile team→BU: manager-less
        // active people (ADMIN + the BU heads) are in the BU base but in no team — exactly why the two
        // populations are tracked in separate fields.
        Assert.Equal(bu01Snap.CompletionDenominator + bu02Snap.CompletionDenominator, groupSnap.CompletionDenominator);
        Assert.Equal(groupSnap.CompletionDenominator, portfolioSnap.CompletionDenominator);
        Assert.Equal(portfolioSnap.CompletionDenominator, allHig.CompletionDenominator);
        Assert.Equal(14, allHig.CompletionDenominator); // all active, invited people across both BUs

        // Recompute BU01's dimension-0 mean independently from raw rows (score of record = manager score,
        // populated for moderated AND auto-adopted) — must equal the stored snapshot value (desync guard).
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var firstDim = await db.Dimensions.OrderBy(d => d.DisplayOrder).FirstAsync();
            var bu01PersonIds = await db.People.Where(p => p.BusinessUnitId == bu01).Select(p => p.Id).ToListAsync();
            var rawManagerScores = await db.Set<AssessmentScore>()
                .Where(s => s.DimensionId == firstDim.Id && s.ManagerScore != null)
                .Join(db.Set<Assessment>().Where(a => a.CycleId == cycleId && bu01PersonIds.Contains(a.PersonId)),
                    s => s.AssessmentId, a => a.Id, (s, a) => s.ManagerScore!.Value)
                .ToListAsync();
            var recomputed = MaturityScoring.Mean(rawManagerScores);
            Assert.Equal(recomputed, bu01Snap.PerDimensionMean[firstDim.Key]);
            Assert.Equal(7, rawManagerScores.Count); // BU01 scored population, independently counted
        }
    }

    [Fact]
    public async Task A_published_verdict_is_frozen_and_survives_shrinking_the_node_below_four_after_close()
    {
        var (bus, people, seed) = TreeFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitTreeAsync();
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);

        var mgrB = await PersonIdAsync("MGRB");
        var before = await SnapshotAsync(cycleId, OrgNodeType.Team, mgrB);
        Assert.Equal(4, before.N);
        Assert.False(before.Suppressed); // published at close (n=4)

        // Shrink Team(MGRB) below 4 AFTER close — deactivate two members. A live recompute would now
        // suppress it (N<4), but the snapshot is immutable, so the frozen verdict must NOT change (FR-071).
        await DeactivateAsync("EB1", "EB2");

        var after = await SnapshotAsync(cycleId, OrgNodeType.Team, mgrB);
        Assert.Equal(4, after.N);            // frozen headcount unchanged
        Assert.False(after.Suppressed);      // still published — history is not retro-suppressed
        Assert.Equal(before.Id, after.Id);   // same immutable row, never rewritten
    }

    // === FR-070 departure escalation × auto-adoption ==============================================

    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) DepartureFixture()
    {
        var bus = new[] { Snap.Bu("BU01") };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("DIR", "BU01"),
            Snap.Person("ACTIVE1", "BU01", managerExternalRef: "DIR"), // keeps DIR a line manager after MGRD departs
            Snap.Person("MGRD", "BU01", managerExternalRef: "DIR"),    // departs mid-cycle
            Snap.Person("EMP_D1", "BU01", managerExternalRef: "MGRD"), // moderated by escalated DIR
            Snap.Person("EMP_D2", "BU01", managerExternalRef: "MGRD"), // left unmoderated → auto-adopt
        };
        var seed = new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("DIR", role: "Manager"),
            Snap.SeedUser("ACTIVE1", role: "Individual"),
            Snap.SeedUser("MGRD", role: "Manager"),
            Snap.SeedUser("EMP_D1", role: "Individual"),
            Snap.SeedUser("EMP_D2", role: "Individual"),
        };
        return (bus, people, seed);
    }

    [Fact]
    public async Task A_departed_managers_report_can_be_moderated_by_the_escalated_reviewer_others_auto_adopt()
    {
        var (bus, people, seed) = DepartureFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        await SubmitUniformAsync("EMP_D1", 2);
        await SubmitUniformAsync("EMP_D2", 3);
        await DeactivateAsync("MGRD"); // the line manager departs with two pending reviews

        // Reviewer of record for EMP_D1 escalates past the inactive MGRD to DIR (ChainResolver, reused by
        // the seam) — DIR can moderate until close.
        await ModerateUniformAsync("DIR", "EMP_D1", 2);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycleId}/close", null)).StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var d1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP_D1");
        var d2 = await db.People.SingleAsync(p => p.ExternalRef == "EMP_D2");
        var a1 = await db.Set<Assessment>().SingleAsync(a => a.PersonId == d1.Id);
        var a2 = await db.Set<Assessment>().SingleAsync(a => a.PersonId == d2.Id);

        Assert.Equal(AssessmentState.Moderated, a1.State);   // escalated moderation stands
        Assert.False(a1.Unmoderated);
        Assert.Equal(AssessmentState.AutoAdopted, a2.State); // still-unmoderated at close → auto-adopt
        Assert.True(a2.Unmoderated);
    }

    // === B1 / Q-023: cross-BU and manager-less people are teamless (close must not 500) =============

    [Fact]
    public async Task A_cross_bu_managed_report_is_teamless_counted_only_in_its_home_bu()
    {
        // HAP-EDGE-XBU-REPORT shape: XREP is HOMED in BU02 but MANAGED by BU01's LEAD1. Pre-B1 this put a
        // BU02 person into a BU01-rooted team → Σ(team n) could exceed a BU's n → SuppressionEvaluator throws
        // (500 close) or freezes a wrong verdict. Q-023: XREP is teamless — counted in BU02 only.
        var bus = new[]
        {
            Snap.Bu("BU01", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU02", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("LEAD1", "BU01"),                                   // BU01 head (manager-less)
            Snap.Person("E1", "BU01", managerExternalRef: "LEAD1"),
            Snap.Person("E2", "BU01", managerExternalRef: "LEAD1"),
            Snap.Person("E3", "BU01", managerExternalRef: "LEAD1"),
            Snap.Person("E4", "BU01", managerExternalRef: "LEAD1"),
            Snap.Person("BHEAD", "BU02"),                                   // BU02 head
            Snap.Person("XREP", "BU02", managerExternalRef: "LEAD1"),       // HOME BU02, managed cross-BU by LEAD1
            Snap.Person("B1", "BU02", managerExternalRef: "BHEAD"),
            Snap.Person("B2", "BU02", managerExternalRef: "BHEAD"),
            Snap.Person("B3", "BU02", managerExternalRef: "BHEAD"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        foreach (var r in new[] { "E1", "E2", "E3", "E4", "XREP", "B1", "B2", "B3" })
        {
            await SubmitUniformAsync(r, 2);
        }

        // Must be 200, not 500 — the pre-B1 bug threw inside suppression on Σchild>parent.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycleId}/close", null)).StatusCode);

        var lead1 = await PersonIdAsync("LEAD1");
        var bhead = await PersonIdAsync("BHEAD");
        Guid bu01, bu02;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            bu01 = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).Id;
            bu02 = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU02")).Id;
        }

        var teamLead1 = await SnapshotAsync(cycleId, OrgNodeType.Team, lead1);
        var teamBhead = await SnapshotAsync(cycleId, OrgNodeType.Team, bhead);
        var bu01Snap = await SnapshotAsync(cycleId, OrgNodeType.Bu, bu01);
        var bu02Snap = await SnapshotAsync(cycleId, OrgNodeType.Bu, bu02);

        Assert.Equal(4, teamLead1.N);   // E1..E4 — XREP EXCLUDED though LEAD1 manages it (different home BU)
        Assert.Equal(3, teamBhead.N);   // B1..B3
        Assert.Equal(4, bu01Snap.N);    // E1..E4 (LEAD1 didn't submit)
        Assert.Equal(4, bu02Snap.N);    // B1..B3 + XREP — XREP counted in its HOME BU

        // Generalized reconciliation (the SPEC-CORRECTED invariant): Σ(team n rooted in a BU) equals the
        // BU's TEAM-HOMED scored population, not its whole n. BU01: 4 = 4 (all team-homed). BU02: 3 team-homed
        // + 1 teamless (XREP) = 4.
        Assert.Equal(teamLead1.N, bu01Snap.N);          // BU01 has no teamless scored person
        Assert.Equal(teamBhead.N + 1, bu02Snap.N);      // BU02's one teamless scored person is XREP
    }

    [Fact]
    public async Task A_manager_less_scored_bu_head_is_teamless_counted_only_in_its_home_bu()
    {
        // A BU head who SUBMITS (manager-less, so no same-BU manager) is teamless: in BU/Group/AllHig, in no
        // Team. Pre-B1 the existing tests only passed because BU heads never submitted (fixture-luck).
        var bus = new[] { Snap.Bu("BU01", group: "Group A", portfolio: "Portfolio 1") };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("HEAD", "BU01"),                              // manager-less head — will submit
            Snap.Person("E1", "BU01", managerExternalRef: "HEAD"),
            Snap.Person("E2", "BU01", managerExternalRef: "HEAD"),
            Snap.Person("E3", "BU01", managerExternalRef: "HEAD"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        foreach (var r in new[] { "HEAD", "E1", "E2", "E3" }) // the HEAD submits too
        {
            await SubmitUniformAsync(r, 2);
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycleId}/close", null)).StatusCode);

        var headId = await PersonIdAsync("HEAD");
        Guid bu01;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            bu01 = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).Id;
        }

        var teamHead = await SnapshotAsync(cycleId, OrgNodeType.Team, headId);
        var bu01Snap = await SnapshotAsync(cycleId, OrgNodeType.Bu, bu01);
        var allHig = await SnapshotAsync(cycleId, OrgNodeType.AllHig, null);

        Assert.Equal(3, teamHead.N);    // E1..E3 — the HEAD is NOT a member of their own team
        Assert.Equal(4, bu01Snap.N);    // HEAD + E1..E3 — the scored manager-less head IS counted in the BU
        Assert.Equal(4, allHig.N);
        Assert.Equal(teamHead.N + 1, bu01Snap.N); // the one teamless scored person is the HEAD
    }

    // === HAP-11 BR1: the cross-level differencing defence runs at CLOSE, frozen into the snapshot =========

    [Fact]
    public async Task A_sub_four_branch_under_a_published_ancestor_is_cross_level_suppressed_in_the_frozen_snapshot()
    {
        // An explicitly ADVERSARIAL close fixture (red-team round-2 caveat): a sub-4 branch (BU_C=3, a
        // single-child Group B / Portfolio 2 chain) sits under the PUBLISHED all-HIG root, alongside a
        // published Group A (BU_A=4 + BU_B=5 = 9). Per-parent suppression alone would publish BU_A(4) — and
        // then AllHig(12) − BU_A − BU_B = 3 would recover the sub-4 branch. The cross-level `Close` must run at
        // close and FREEZE the stronger verdict: BU_A suppressed too, so no published snapshot reveals BU_C.
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group B", portfolio: "Portfolio 2"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("MGR_A", "BU_A"),
            Snap.Person("MGR_B", "BU_B"),
            Snap.Person("HEAD_C", "BU_C"),
        };
        for (var i = 1; i <= 4; i++) people.Add(Snap.Person($"E_A{i}", "BU_A", managerExternalRef: "MGR_A"));
        for (var i = 1; i <= 5; i++) people.Add(Snap.Person($"E_B{i}", "BU_B", managerExternalRef: "MGR_B"));
        for (var i = 1; i <= 3; i++) people.Add(Snap.Person($"E_C{i}", "BU_C", managerExternalRef: "HEAD_C"));

        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people.Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));

        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        foreach (var r in Enumerable.Range(1, 4).Select(i => $"E_A{i}")
                     .Concat(Enumerable.Range(1, 5).Select(i => $"E_B{i}"))
                     .Concat(Enumerable.Range(1, 3).Select(i => $"E_C{i}")))
        {
            await SubmitUniformAsync(r, 2);
        }
        foreach (var (mgr, report) in new[] { ("MGR_A", 4), ("MGR_B", 5), ("HEAD_C", 3) }
                     .SelectMany(t => Enumerable.Range(1, t.Item2)
                         .Select(i => (t.Item1, $"E_{t.Item1[^1]}{i}"))))
        {
            await ModerateUniformAsync(mgr, report, 2);
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycleId}/close", null)).StatusCode);

        Guid buA, buB, buC, groupA, groupB;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            buA = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id;
            buB = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_B")).Id;
            buC = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_C")).Id;
            groupA = (await db.Groups.SingleAsync(g => g.Name == "Group A")).Id;
            groupB = (await db.Groups.SingleAsync(g => g.Name == "Group B")).Id;
        }

        var buASnap = await SnapshotAsync(cycleId, OrgNodeType.Bu, buA);
        var buBSnap = await SnapshotAsync(cycleId, OrgNodeType.Bu, buB);
        var buCSnap = await SnapshotAsync(cycleId, OrgNodeType.Bu, buC);
        var groupASnap = await SnapshotAsync(cycleId, OrgNodeType.Group, groupA);
        var groupBSnap = await SnapshotAsync(cycleId, OrgNodeType.Group, groupB);
        var allHig = await SnapshotAsync(cycleId, OrgNodeType.AllHig, null);

        // The sub-4 branch is frozen suppressed.
        Assert.True(buCSnap.Suppressed);
        Assert.True(groupBSnap.Suppressed);
        // The cross-level Close ran AT CLOSE: BU_A (n=4, ≥ threshold, per-parent-published) is frozen
        // SUPPRESSED because AllHig − BU_A − BU_B would otherwise recover BU_C. This is the proof the
        // hierarchy-global defence is in the snapshot, not only in the live read.
        Assert.True(buASnap.Suppressed);
        Assert.Equal("Complement", buASnap.SuppressionReason);
        Assert.True(groupASnap.Suppressed); // equal-membership with its portfolio, collapsed
        // …while the useful high-level total and the safe sibling survive, and neither reveals BU_C:
        // AllHig(12) − BU_B(5) = 7 = BU_A + BU_C, a two-unknown sum, never the single figure 3.
        Assert.False(allHig.Suppressed);
        Assert.Equal(12, allHig.N);
        Assert.False(buBSnap.Suppressed);
        Assert.Equal(5, buBSnap.N);
    }
}
