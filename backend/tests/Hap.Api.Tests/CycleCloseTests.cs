using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Assessments;
using Hap.Domain.Rollups;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-10 cycle-close acceptance criteria (FR-068 auto-adopt + unmoderated %/calibration exclusion,
/// FR-069/070 leave + departure, FR-015/016 mean+floor, FR-014/071 frozen suppression, FR-024/§3.5
/// scored-vs-completion split). Drives the real endpoints (self submit → manager moderate → admin close)
/// against the disposable Postgres, then inspects assessment states and the frozen
/// <c>rollup_snapshots</c>. The pure maths are unit-tested in <c>Hap.Domain.Tests</c>; this pins the
/// orchestration, persistence, and the L3 reconciliation/suppression guarantees end-to-end.
/// </summary>
[Collection("hap-db")]
public sealed class CycleCloseTests
{
    private readonly HapApiFactory _factory;

    public CycleCloseTests(HapApiFactory factory) => _factory = factory;

    // --- setup helpers ---------------------------------------------------------------------------

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync(
        IEnumerable<DirectoryBu> bus, IEnumerable<DirectoryPerson> persons, IEnumerable<SeedUserRecord> seedUsers)
    {
        await _factory.ResetAsync();
        var buList = bus.ToList();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(buList, persons));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            foreach (var bu in await db.BusinessUnits.ToListAsync())
            {
                bu.SetOnboarded(true); // onboard every BU in the fixture so everyone is invited
            }
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seedUsers.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid frameworkVersionId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, name, true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return cycle.Id;
    }

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    /// <summary>Submits a full assessment for <paramref name="externalRef"/> with the given per-dimension
    /// scores (length 7, in the form's dimension order). Leaves it Submitted, ready for moderation.</summary>
    private async Task SubmitAsync(string externalRef, params int[] perDimensionScores)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var dims = form.Dimensions.ToList();
        Assert.Equal(perDimensionScores.Length, dims.Count);
        var entries = dims.Select((d, i) => new ScoreEntry(d.DimensionId, perDimensionScores[i], null)).ToList();
        await client.PutAsJsonAsync("/api/me/assessment/scores", new UpsertScoresRequest(entries));
        var submit = await client.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    /// <summary>Submits every dimension at the same score.</summary>
    private Task SubmitUniformAsync(string externalRef, int score) =>
        SubmitAsync(externalRef, Enumerable.Repeat(score, 7).ToArray());

    /// <summary>Signs in as <paramref name="managerRef"/> and moderates <paramref name="subjectRef"/>'s
    /// assessment, setting every dimension to <paramref name="managerScore"/> (with a comment so a Δ≥2
    /// moderation is accepted).</summary>
    private async Task ModerateUniformAsync(string managerRef, string subjectRef, int managerScore)
    {
        var subjectId = await PersonIdAsync(subjectRef);
        var assessmentId = await AssessmentIdAsync(subjectRef);
        var manager = await ClientAsync(managerRef);
        var view = (await (await manager.GetAsync($"/api/team/members/{subjectId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions
            .Select(d => new ModerateDecision(d.DimensionId, managerScore, "moderated at close-test"))
            .ToList();
        var resp = await manager.PutAsJsonAsync($"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(decisions));
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    private static Task<HttpResponseMessage> CloseAsync(HttpClient admin, Guid cycleId) =>
        admin.PostAsync($"/api/cycles/{cycleId}/close", null);

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

    private async Task<(AssessmentState State, bool Unmoderated, int[] ManagerScores, int[] SelfScores)> AssessmentAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        var a = await db.Set<Assessment>().SingleAsync(x => x.PersonId == personId);
        var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == a.Id).ToListAsync();
        return (a.State, a.Unmoderated,
            scores.Select(s => s.ManagerScore ?? -1).ToArray(),
            scores.Select(s => s.SelfScore).ToArray());
    }

    private async Task<RollupSnapshot> SnapshotAsync(Guid cycleId, OrgNodeType type, Guid? nodeRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.RollupSnapshots.AsNoTracking()
            .SingleAsync(s => s.CycleId == cycleId && s.OrgNodeType == type && s.OrgNodeRef == nodeRef);
    }

    private async Task<List<RollupSnapshot>> SnapshotsAsync(Guid cycleId, OrgNodeType type)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.RollupSnapshots.AsNoTracking()
            .Where(s => s.CycleId == cycleId && s.OrgNodeType == type).ToListAsync();
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

    // A minimal single-BU fixture: MGR1 (BU head, manager-less) + four reports EMP1..EMP4.
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

    // === FR-068 auto-adoption =====================================================================

    [Fact]
    public async Task Close_auto_adopts_unmoderated_submissions_and_leaves_moderated_ones_untouched()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        await SubmitUniformAsync("EMP1", 2);       // will be moderated
        await SubmitUniformAsync("EMP2", 3);       // will be left unmoderated → auto-adopt
        await ModerateUniformAsync("MGR1", "EMP1", 1); // manager overrides EMP1's 2 down to 1 (Δ1)

        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(admin, cycleId)).StatusCode);

        var emp1 = await AssessmentAsync("EMP1");
        Assert.Equal(AssessmentState.Moderated, emp1.State);
        Assert.False(emp1.Unmoderated);
        Assert.All(emp1.ManagerScores, m => Assert.Equal(1, m)); // manager score of record preserved

        var emp2 = await AssessmentAsync("EMP2");
        Assert.Equal(AssessmentState.AutoAdopted, emp2.State);
        Assert.True(emp2.Unmoderated);
        Assert.All(emp2.ManagerScores, m => Assert.Equal(3, m)); // self copied to score of record
    }

    [Fact]
    public async Task An_assessment_never_submitted_is_not_auto_adopted_at_close()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitUniformAsync("EMP1", 2); // only EMP1 submits; EMP2..4 never start

        await CloseAsync(admin, cycleId);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        // EMP2 has no assessment row at all (viewing the empty form creates nothing, HAP-8) — auto-adopt
        // only touches Submitted rows, so nothing is fabricated for a non-participant.
        var emp2Id = await PersonIdAsync("EMP2");
        Assert.False(await db.Set<Assessment>().AnyAsync(a => a.PersonId == emp2Id));
    }

    // === FR-015/016 snapshots: hand-computed spot values ==========================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Snapshots_carry_hand_computed_mean_floor_completion_and_unmoderated_pct()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        // Four reports submit; MGR1 (the BU head) does not participate. Scores chosen for clean maths:
        await SubmitAsync("EMP1", 3, 3, 3, 3, 3, 3, 3); // floor 3
        await SubmitAsync("EMP2", 2, 2, 2, 2, 2, 2, 2); // floor 2
        await SubmitAsync("EMP3", 1, 1, 1, 1, 1, 1, 1); // floor 1
        await SubmitAsync("EMP4", 0, 3, 3, 3, 3, 3, 3); // floor 0
        // Moderate none → all four auto-adopt (score of record = self).
        await CloseAsync(admin, cycleId);

        var teamRef = await PersonIdAsync("MGR1");
        var team = await SnapshotAsync(cycleId, OrgNodeType.Team, teamRef);

        Assert.Equal(4, team.N);
        // One dimension (the one where EMP4 scored 0) has mean (3+2+1+0)/4 = 1.5; the other six have
        // (3+2+1+3)/4 = 2.25. Assert by value distribution — dimension KEY order is not the form's
        // DisplayOrder, so we don't assume which key is the varied one.
        Assert.Equal(7, team.PerDimensionMean.Count);
        var means = team.PerDimensionMean.Values.OrderBy(v => v).ToList();
        Assert.Equal(1.5, means[0]);
        Assert.All(means.Skip(1), v => Assert.Equal(2.25, v));
        // Floor distribution: one each at 3,2,1,0.
        Assert.Equal(1, team.FloorLevelDistribution[3]);
        Assert.Equal(1, team.FloorLevelDistribution[2]);
        Assert.Equal(1, team.FloorLevelDistribution[1]);
        Assert.Equal(1, team.FloorLevelDistribution[0]);
        Assert.Equal(1.0, team.CompletionPct);      // 4 of 4 active reports submitted
        Assert.Equal(1.0, team.UnmoderatedPct);      // all four auto-adopted
        Assert.Equal(4, team.CompletionDenominator);
        Assert.Empty(team.CalibrationDelta);         // all auto-adopted → no calibration signal
    }

    // === FR-024 / §3.5: scored population ≠ completion denominator =================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_mid_cycle_leaver_stays_in_the_scored_population_but_leaves_the_completion_base()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        await SubmitUniformAsync("EMP1", 2);
        await SubmitUniformAsync("EMP2", 2);
        await SubmitUniformAsync("EMP3", 0); // this one will leave before close
        // EMP4 is active but never submits (in the completion base, not the scored population).
        await DeactivateAsync("EMP3"); // leaver: submitted work retained, but out of the completion base

        await CloseAsync(admin, cycleId);

        var team = await SnapshotAsync(cycleId, OrgNodeType.Team, await PersonIdAsync("MGR1"));

        // Scored population = 3 (EMP1, EMP2, EMP3-the-leaver). Completion base = 3 active invited
        // (EMP1, EMP2, EMP4) of whom 2 submitted. scored-n (3) ≠ completion-n (3 denom, 2 numerator).
        Assert.Equal(3, team.N);                         // leaver still counted
        Assert.Equal(3, team.CompletionDenominator);     // EMP1, EMP2, EMP4 — leaver excluded
        Assert.Equal(2d / 3d, team.CompletionPct);       // only EMP1, EMP2 of the active base submitted
        var d0 = team.PerDimensionMean[team.PerDimensionMean.Keys.First()];
        Assert.Equal(1.33, d0);                          // (2 + 2 + 0) / 3 — leaver still in the mean
        Assert.Equal(1, team.FloorLevelDistribution[0]); // leaver's floor still counted
    }

    // === FR-068 calibration delta excludes auto-adopted (integration) =============================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Calibration_delta_is_unchanged_by_an_auto_adopted_member()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);

        await SubmitUniformAsync("EMP1", 3); // moderated to 1 → |Δ| = 2
        await SubmitUniformAsync("EMP2", 2); // moderated to 2 → |Δ| = 0
        await SubmitUniformAsync("EMP3", 0); // NOT moderated → auto-adopts, must not enter the delta
        await ModerateUniformAsync("MGR1", "EMP1", 1);
        await ModerateUniformAsync("MGR1", "EMP2", 2);

        await CloseAsync(admin, cycleId);

        var team = await SnapshotAsync(cycleId, OrgNodeType.Team, await PersonIdAsync("MGR1"));
        // Delta per dimension over the two MODERATED rows only = (2 + 0) / 2 = 1.0; the auto-adopted EMP3
        // (Δ0) is excluded, so it neither adds a zero nor changes the denominator.
        Assert.All(team.CalibrationDelta.Values, v => Assert.Equal(1.0, v));
        Assert.NotEmpty(team.CalibrationDelta);
    }

    // === B3: live-DB append-only trigger rejection (mirrors OrgOverrideAuditTests) ================

    private async Task<Guid> CloseWithOneSnapshotAsync()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitUniformAsync("EMP1", 2);
        await CloseAsync(admin, cycleId);
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.True(await db.RollupSnapshots.AnyAsync(s => s.CycleId == cycleId)); // rows exist to reject against
        return cycleId;
    }

    private async Task<int> SnapshotCountAsync(Guid cycleId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.RollupSnapshots.CountAsync(s => s.CycleId == cycleId);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Database_rejects_raw_update_of_a_rollup_snapshot()
    {
        var cycleId = await CloseWithOneSnapshotAsync();
        // Capture a real field value — a successful UPDATE would change N without changing the row COUNT,
        // so re-reading N (not the count) is what actually proves the write was rejected.
        Guid snapId;
        int nBefore;
        using (var read = _factory.NewScope())
        {
            var db = read.ServiceProvider.GetRequiredService<HapDbContext>();
            var snap = await db.RollupSnapshots.AsNoTracking().FirstAsync(s => s.CycleId == cycleId);
            snapId = snap.Id;
            nBefore = snap.N;
        }

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            // Quoted PascalCase column so it reaches execution and fires the row trigger.
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE rollup_snapshots SET \"N\" = \"N\" + 1;"));
            Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        using (var after = _factory.NewScope())
        {
            var db = after.ServiceProvider.GetRequiredService<HapDbContext>();
            var reread = await db.RollupSnapshots.AsNoTracking().SingleAsync(s => s.Id == snapId);
            Assert.Equal(nBefore, reread.N); // the value the UPDATE tried to change is untouched
        }
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Database_rejects_raw_delete_of_a_rollup_snapshot()
    {
        var cycleId = await CloseWithOneSnapshotAsync();
        var before = await SnapshotCountAsync(cycleId);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM rollup_snapshots;"));
            Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await SnapshotCountAsync(cycleId)); // frozen history survives the delete attempt
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Database_rejects_raw_truncate_of_rollup_snapshots()
    {
        var cycleId = await CloseWithOneSnapshotAsync();
        var before = await SnapshotCountAsync(cycleId);

        // A row trigger does not fire on TRUNCATE — this proves the dedicated statement-level trigger.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("TRUNCATE rollup_snapshots;"));
            Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await SnapshotCountAsync(cycleId));
    }

    // === F3: a frozen snapshot is byte-for-byte unchanged by a later override re-moderation ========

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_post_close_override_re_moderation_never_alters_the_frozen_snapshot()
    {
        var (bus, people, seed) = SingleTeamFixture();
        var (admin, fvId) = await SeedAsync(bus, people, seed);
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitUniformAsync("EMP1", 3); // no moderation → auto-adopts at close (score of record 3)
        await SubmitUniformAsync("EMP2", 2);
        await SubmitUniformAsync("EMP3", 2);
        await SubmitUniformAsync("EMP4", 2);
        await CloseAsync(admin, cycleId);

        var teamRef = await PersonIdAsync("MGR1");
        var frozen = await SnapshotAsync(cycleId, OrgNodeType.Team, teamRef);

        // Grant EMP1 a post-close late override, then genuinely re-moderate the auto-adopted assessment
        // down to 0 (Q-022) — a live recompute would now change the team mean/floor materially.
        var emp1Id = await PersonIdAsync("EMP1");
        var granted = await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        await ModerateUniformAsync("MGR1", "EMP1", 0);

        var afterState = await AssessmentAsync("EMP1");
        Assert.Equal(AssessmentState.Moderated, afterState.State); // the re-moderation really happened
        Assert.False(afterState.Unmoderated);

        // The snapshot taken AT CLOSE is immutable history — byte-for-byte identical. Its frozen figures
        // legitimately now diverge from the post-override raw rows; that is intended (reconciliation is
        // "as of close", documented in the AC/wiki + Q-022 addendum), not corruption.
        var reread = await SnapshotAsync(cycleId, OrgNodeType.Team, teamRef);
        Assert.Equal(frozen.Id, reread.Id);
        Assert.Equal(frozen.N, reread.N);
        Assert.Equal(frozen.CompletionDenominator, reread.CompletionDenominator);
        Assert.Equal(frozen.CompletionPct, reread.CompletionPct);
        Assert.Equal(frozen.UnmoderatedPct, reread.UnmoderatedPct);
        Assert.Equal(frozen.Suppressed, reread.Suppressed);
        Assert.Equal(frozen.SuppressionReason, reread.SuppressionReason);
        Assert.Equal(frozen.CreatedAt, reread.CreatedAt);
        Assert.Equal(frozen.PerDimensionMean.OrderBy(k => k.Key), reread.PerDimensionMean.OrderBy(k => k.Key));
        Assert.Equal(frozen.FloorLevelDistribution.OrderBy(k => k.Key), reread.FloorLevelDistribution.OrderBy(k => k.Key));
        Assert.Equal(frozen.CalibrationDelta.OrderBy(k => k.Key), reread.CalibrationDelta.OrderBy(k => k.Key));
    }
}
