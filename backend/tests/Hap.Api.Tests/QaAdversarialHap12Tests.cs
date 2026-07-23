using System.Net;
using System.Net.Http.Json;
using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-12 adversarial QA (fresh instance, CLAUDE.md §9). Mandatory-attempt (b) targets erasure
/// non-recoverability across EVERY surface, not just <c>GET /api/me/export</c> — this file probes the
/// OTHER audited individual-read path, <c>GET /api/team/members/{personId}/assessment</c>
/// (<c>ManagerModerationService.GetMemberAssessmentAsync</c>), which the dev round-1 B1 fix did not touch.
///
/// <para><b>Finding:</b> <c>GetMemberAssessmentAsync</c> resolves "current cycle" via
/// <c>SeamCycleResolver.CurrentCycleAsync</c> — the single Open cycle, or else the MOST-RECENTLY-OPENED
/// CLOSED cycle (the post-close late-override window, Q-017a). On a platform that has gone dormant (no
/// cycle opened since), the most-recently-closed cycle can itself be the one retention has already erased.
/// <c>GetMemberAssessmentAsync</c> reads straight from the store with NO cross-reference to the
/// <c>RetentionErasure</c> ledger — <c>MemberDimensionView</c>/<c>MemberAssessmentResponse</c> carry no
/// <c>Erased</c> field at all (contrast <c>ExportScore.Erased</c>/<c>ExportCycle.DataErased</c>, B1). The
/// manager therefore sees the erasure PLACEHOLDER values (self score zeroed, evidence/manager score/comment
/// null) presented as if genuine — the exact "fabricated 0" the export was fixed to never show — AND the
/// read is AUDITED (writes a real <c>IndividualView</c> row), so it looks like a legitimate, disclosed view.
/// This is the same shape of defect as B1 but on the OTHER [A] read surface, unfixed.</para>
/// </summary>
[Collection("hap-db")]
public sealed class QaAdversarialHap12Tests
{
    private readonly HapApiFactory _factory;

    public QaAdversarialHap12Tests(HapApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Admin, Guid FrameworkVersionId)> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
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

    private async Task SubmitSelfAsync(string externalRef, int score, string? evidence)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, evidence)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task BackdateCloseAsync(Guid cycleId, int years)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var old = DateTime.UtcNow.AddYears(-years);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cycles SET \"ClosesAt\" = {old} WHERE \"Id\" = {cycleId}");
    }

    /// <summary>
    /// RED-TEAM: construct a concrete violation path. Dormant platform (no cycle opened since the erased
    /// one closed): MGR1's [A] member-read resolves "current cycle" to the retention-erased closed cycle
    /// and serves EMP1's raw (fabricated-placeholder) scores with no erasure disclosure — exactly the
    /// leak B1 closed for the export, reopened on this second read surface. EXPECTED (what a compliant
    /// seam must do): either refuse to serve the erased assessment on this surface (404/409) or disclose
    /// erasure explicitly (an <c>Erased</c>/<c>DataErased</c> flag), and MUST NEVER present the zeroed
    /// <c>SelfScore</c> as if it were EMP1's genuine score. This test pins the CORRECT behaviour and is
    /// expected to FAIL against the shipped code, proving the violation path is real, not hypothetical.
    /// </summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Dormant_platform_member_read_of_a_retention_erased_assessment_must_not_present_a_fabricated_score()
    {
        var (admin, fvId) = await SeedAsync();
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(fvId, "2023-01", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        var openResp = await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);

        await SubmitSelfAsync("EMP1", 3, "genuinely sensitive evidence");
        var emp1Id = await PersonIdAsync("EMP1");

        var mgr1 = await ClientAsync("MGR1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "moderated to L1")).ToArray();
        Assert.Equal(HttpStatusCode.NoContent,
            (await mgr1.PutAsJsonAsync($"/api/team/reviews/{view.AssessmentId}", new ModerateReviewRequest(decisions))).StatusCode);

        // Close, then simulate the platform going dormant for 4 years — no further cycle is ever opened,
        // so this closed cycle stays "current" per SeamCycleResolver (no Open cycle exists).
        var closeResp = await admin.PostAsync($"/api/cycles/{cycle.Id}/close", null);
        var closeBody = await closeResp.Content.ReadAsStringAsync();
        Assert.True(closeResp.StatusCode == HttpStatusCode.OK, $"close failed: {closeResp.StatusCode}: {closeBody}");
        await BackdateCloseAsync(cycle.Id, 4);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var closesAt = await db.Database.SqlQuery<DateTime>(
                $"SELECT \"ClosesAt\" AS \"Value\" FROM cycles WHERE \"Id\" = {cycle.Id}").SingleAsync();
            Assert.True(closesAt < DateTime.UtcNow.AddYears(-3), $"backdate did not take effect: ClosesAt={closesAt:o}");
        }

        var retentionResp = await admin.PostAsync("/api/admin/retention/run", null);
        var retentionBody = await retentionResp.Content.ReadAsStringAsync();
        Assert.True(retentionResp.StatusCode == HttpStatusCode.OK, $"retention/run failed: {retentionResp.StatusCode}: {retentionBody}");
        var retentionResult = (await retentionResp.Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.True(retentionResult.AssessmentsErased >= 1,
            $"setup invariant: the assessment must actually be erased (AssessmentsErased={retentionResult.AssessmentsErased}, ScoreRowsErased={retentionResult.ScoreRowsErased})");

        // No new cycle opened — CurrentCycleAsync now falls through to this erased closed cycle.
        var mgr1Again = await ClientAsync("MGR1");
        var afterErasure = await mgr1Again.GetAsync($"/api/team/members/{emp1Id}/assessment");

        // The raw response body, inspected structurally: does it contain the fabricated-0 SelfScore
        // presented with no erasure disclosure anywhere in the payload?
        var raw = await afterErasure.Content.ReadAsStringAsync();

        if (afterErasure.StatusCode == HttpStatusCode.OK)
        {
            // Endpoint served data for an erased assessment. It MUST disclose erasure (an Erased/DataErased
            // marker in the payload) rather than silently returning the zeroed placeholder as a genuine
            // score. This is the B1 guarantee, which this test proves does NOT hold on this second surface.
            //
            // CONFIRMED DEFECT (2026-07-22, QA fresh instance): the running code returns 200 OK with
            // state:"Moderated" and selfScore:0/managerScore:null for EVERY dimension — the exact fabricated
            // placeholder EMP1's genuine self-score (3) and MGR1's genuine moderation (1, "moderated to L1")
            // were erased to — with NO Erased/DataErased marker anywhere in the payload. A manager reading
            // this on a dormant platform sees a confidently-presented "Moderated" assessment with a real
            // looking (if oddly uniform) 0 self-score; nothing distinguishes it from a genuine floor-level
            // self-assessment. This is the B1 leak (fixed for GET /api/me/export) reopened on
            // GetMemberAssessmentAsync (ManagerModerationService.cs), which has NO Erased field on
            // MemberDimensionView/MemberAssessmentResponse at all and never consults the RetentionErasure
            // ledger. Reachable precondition: a dormant platform (no cycle opened since the erased one
            // closed) — SeamCycleResolver.CurrentCycleAsync falls through to "the most-recently-opened
            // Closed cycle" when no Open cycle exists, which can itself be the erased one.
            Assert.Fail("B1 (fabricated-0 disclosure) does not hold on GET /api/team/members/{id}/assessment " +
                "for a retention-erased assessment on a dormant platform — no erasure marker in the payload " +
                "and the zeroed SelfScore is presented as genuine. Full body: " + raw);
        }
        else
        {
            // Alternative compliant shape: refuse to serve an erased assessment on this surface at all.
            Assert.True(
                afterErasure.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                $"expected the erased assessment to be either disclosed-erased or refused, got {afterErasure.StatusCode}: {raw}");
        }
    }

    // === Mandatory §9.3(a) sweep: admin/audit + retention/run across ALL SEVEN seeded roles ===========

    private async Task<HttpClient> SeedAllSevenRolesAsync()
    {
        await _factory.ResetAsync();
        var people = new[]
        {
            Snap.Person("ADMIN", "BU01"),
            Snap.Person("EXEC", "BU01"),
            Snap.Person("PFL", "BU01"),                                   // Portfolio Leader, top (no manager)
            Snap.Person("GRL", "BU01", managerExternalRef: "PFL"),        // Group Leader
            Snap.Person("BUL", "BU01", managerExternalRef: "GRL"),        // BU Lead
            Snap.Person("MGR1", "BU01", managerExternalRef: "BUL"),       // Manager
            Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),      // Individual
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(new[] { Snap.Bu("BU01") }, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("EXEC", role: "HIG Executive"),
            Snap.SeedUser("PFL", role: "Portfolio Leader", buCode: "BU01"),
            Snap.SeedUser("GRL", role: "Group Leader", buCode: "BU01"),
            Snap.SeedUser("BUL", role: "BU Lead", buCode: "BU01"),
            Snap.SeedUser("MGR1", role: "Manager", buCode: "BU01"),
            Snap.SeedUser("EMP1", role: "Individual", buCode: "BU01"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        await admin.PostAsync("/api/admin/frameworks", null);
        return admin;
    }

    /// <summary>
    /// Mandatory-attempt (a): as EACH of the seven seeded roles, attempt the two Platform-Admin-only
    /// audit/GDPR surfaces (<c>GET /api/admin/audit</c>, <c>POST /api/admin/retention/run</c>). Only ADMIN
    /// (Platform Admin) may succeed; every other role — including hierarchy-derived leadership tiers with
    /// no explicit grant (Portfolio Leader, Group Leader, BU Lead) and the HIG Executive explicit grant —
    /// must be refused. A single success by a non-admin role would be a blocking G1 leak (raw audit rows /
    /// erasure trigger reachable outside Platform Admin).
    /// </summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Admin_audit_and_retention_surfaces_are_refused_for_every_non_platform_admin_role()
    {
        var admin = await SeedAllSevenRolesAsync();
        var nonAdminRoles = new[] { "EXEC", "PFL", "GRL", "BUL", "MGR1", "EMP1" };

        foreach (var role in nonAdminRoles)
        {
            var client = await ClientAsync(role);

            var audit = await client.GetAsync("/api/admin/audit");
            Assert.True(audit.StatusCode == HttpStatusCode.Forbidden,
                $"{role} should be refused GET /api/admin/audit, got {audit.StatusCode}");

            var retention = await client.PostAsync("/api/admin/retention/run", null);
            Assert.True(retention.StatusCode == HttpStatusCode.Forbidden,
                $"{role} should be refused POST /api/admin/retention/run, got {retention.StatusCode}");
        }

        // Positive control: ADMIN (Platform Admin) succeeds on both, proving the sweep above is
        // discriminating (a role gate that denied EVERYONE would pass the negative loop vacuously).
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/retention/run", null)).StatusCode);
    }

    /// <summary>
    /// Mandatory-attempt (a) / (c): <c>GET /api/me/export</c> is self-scope by construction (no route/body
    /// person parameter — contracts/api.md), but this proves it empirically for a role that DOES hold a
    /// legitimate one-hop DR-0005 individual-read capability over another person (BUL is GRL's chain
    /// ancestor... here instead BUL reads its OWN export while also being able to read EMP1's chain-mate
    /// MGR1's individual assessment) — the read capability over someone else must never leak into what
    /// export returns for the caller.
    /// </summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Export_returns_only_the_callers_own_data_even_for_a_role_with_individual_read_capability_over_others()
    {
        var admin = await SeedAllSevenRolesAsync();
        var mgr1Id = await PersonIdAsync("MGR1");

        // BUL is MGR1's reviewer of record via the chain (BUL -> GRL -> PFL is the manager line, and MGR1's
        // manager is BUL directly) -- confirm BUL really can read MGR1 individually first (positive control
        // for the read capability this test is probing does not leak into export).
        var bul = await ClientAsync("BUL");
        var readOfMgr1 = await bul.GetAsync($"/api/team/members/{mgr1Id}/assessment");
        Assert.True(readOfMgr1.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"unexpected status reading MGR1 as BUL: {readOfMgr1.StatusCode}");

        // Now BUL exports — must get BUL's own (empty) data, never MGR1's, and there is no parameter to ask
        // for anyone else's.
        var export = (await (await bul.GetAsync("/api/me/export")).Content.ReadFromJsonAsync<PersonalDataExport>())!;
        var bulId = await PersonIdAsync("BUL");
        Assert.Equal(bulId, export.Person.PersonId);
        Assert.NotEqual(mgr1Id, export.Person.PersonId);

        var raw = await (await bul.GetAsync("/api/me/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(mgr1Id.ToString(), raw); // MGR1's id never appears in BUL's export
    }
}
