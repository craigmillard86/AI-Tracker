using System.Net;
using System.Net.Http.Json;
using Hap.Api.Authorization;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-12 V3 — the G1 privacy rehearsal (quickstart.md "V3 — Privacy spot-checks"; SC-005/SC-006) automated
/// end-to-end. Reproduces the ratified visibility policy on a deterministic engineered hierarchy carrying the
/// DR-0005 canonical refs (<c>HAP-PF-01 → HAP-GRP-01 → HAP-BUL-01 → HAP-SEED-IND</c>), a DR-0006 contractor
/// manager, an above-BU HIG Executive, and a sub-4 team:
/// <list type="bullet">
/// <item><b>Zero individual reads outside the chain, all seven roles</b> — every role attempting an
/// out-of-chain / no-capability individual read gets 404 and writes NO audit row (SC-005).</item>
/// <item><b>DR-0005 one-hop ALLOW</b> — a direct line manager reads their immediate direct report regardless
/// of tier (<c>HAP-PF-01 → HAP-GRP-01</c>, <c>HAP-GRP-01 → HAP-BUL-01</c>), each writing exactly one
/// IndividualView row; a 2+-hop read (<c>HAP-PF-01 → HAP-SEED-IND</c>) is DENIED (transitive closed).</item>
/// <item><b>DR-0006 contractor DENY</b> — a contractor line manager gets no individual-score access; the
/// report escalates to the first employee ancestor.</item>
/// <item><b>Above-BU aggregates-only (FR-025 §2)</b> — a HIG Executive cannot read any individual score but
/// can read the all-HIG aggregate rollup.</item>
/// <item><b>N&lt;4 suppression (SC-006)</b> — a 3-person team's [S] aggregate returns "Suppressed" with no
/// figures.</item>
/// <item><b>Audit tie-in</b> — after an allowed manager view, the Platform-Admin audit search surfaces the
/// IndividualView row (quickstart V3 step 4).</item>
/// </list>
/// This is the automated equivalent of the witnessed V3 run the owner executes on the full synthetic stack;
/// the complement-differencing spot-check (V3 step 3) is exhaustively covered by <c>HierarchySuppressionTests</c>
/// + <c>RollupDashboardTests</c> and documented as a witnessed step in the V3 doc.
/// </summary>
[Collection("hap-db")]
public sealed class PrivacySpotChecksV3Tests
{
    private readonly HapApiFactory _factory;

    public PrivacySpotChecksV3Tests(HapApiFactory factory) => _factory = factory;

    // ---- wire DTOs (camelCase; figures null when suppressed) ---------------------------------------
    private sealed record NodeDto(string NodeType, bool Suppressed, string? SuppressionReason, object? Figures);

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_PF"),
            Snap.Person("EXEC", "BU_PF"),
            Snap.Person("HAP-PF-01", "BU_PF"),                                    // portfolio top (no manager)
            Snap.Person("HAP-GRP-01", "BU_PF", managerExternalRef: "HAP-PF-01"),  // one hop below PF
            Snap.Person("HAP-BUL-01", "BU_PF", managerExternalRef: "HAP-GRP-01"), // one hop below GRP
            Snap.Person("HAP-SEED-IND", "BU_PF", managerExternalRef: "HAP-BUL-01"), // 2+ hops below PF/GRP
            Snap.Person("STRANGER", "BU_PF", managerExternalRef: "HAP-BUL-01"),   // peer of SEED-IND
            Snap.Person("MGR_S", "BU_PF", managerExternalRef: "HAP-BUL-01"),      // manages the sub-4 team
            Snap.Person("S1", "BU_PF", managerExternalRef: "MGR_S"),
            Snap.Person("S2", "BU_PF", managerExternalRef: "MGR_S"),
            Snap.Person("S3", "BU_PF", managerExternalRef: "MGR_S"),
            // DR-0006: contractor CTR-01 manages EMP-C; EMP-C's reviewer of record escalates to HAP-BUL-01.
            Snap.Person("CTR-01", "BU_PF", managerExternalRef: "HAP-BUL-01", employeeType: "Contractor"),
            Snap.Person("EMP-C", "BU_PF", managerExternalRef: "CTR-01"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(new[] { Snap.Bu("BU_PF") }, people));

        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_PF")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }

        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),      // explicit grant
            Snap.SeedUser("EXEC", role: "HIG Executive"),        // explicit grant → all-HIG aggregates only
            Snap.SeedUser("HAP-PF-01", role: "Portfolio Leader", buCode: "BU_PF"), // no grant → plain manager by position
            Snap.SeedUser("HAP-GRP-01", role: "Group Leader", buCode: "BU_PF"),
            Snap.SeedUser("HAP-BUL-01", role: "BU Lead", buCode: "BU_PF"),
            Snap.SeedUser("HAP-SEED-IND", role: "Individual", buCode: "BU_PF"),
            Snap.SeedUser("STRANGER", role: "Individual", buCode: "BU_PF"),
            Snap.SeedUser("MGR_S", role: "Manager", buCode: "BU_PF"),
            Snap.SeedUser("S1", role: "Individual", buCode: "BU_PF"),
            Snap.SeedUser("S2", role: "Individual", buCode: "BU_PF"),
            Snap.SeedUser("S3", role: "Individual", buCode: "BU_PF"),
            Snap.SeedUser("CTR-01", role: "Manager", buCode: "BU_PF"),
            Snap.SeedUser("EMP-C", role: "Individual", buCode: "BU_PF"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(seed!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        // The subjects that get READ in the allowed direction need a submitted assessment; the sub-4 team
        // (S1..S3) must be scored for the suppression check.
        foreach (var r in new[] { "HAP-GRP-01", "HAP-BUL-01", "HAP-SEED-IND", "EMP-C", "S1", "S2", "S3" })
        {
            await SubmitSelfAsync(r, 2);
        }
        return admin;
    }

    private async Task SubmitSelfAsync(string externalRef, int score)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
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

    private async Task<int> IndividualViewCountAsync(Guid subjectPersonId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.IndividualView && a.SubjectPersonId == subjectPersonId);
    }

    private static Task<HttpResponseMessage> ReadMemberAsync(HttpClient client, Guid subjectId) =>
        client.GetAsync($"/api/team/members/{subjectId}/assessment");

    // === Zero reads outside the chain — all seven roles (SC-005) ==================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task No_role_can_read_an_individual_score_outside_its_chain_and_no_such_read_is_audited()
    {
        await SeedAsync();
        var seedIndId = await PersonIdAsync("HAP-SEED-IND");
        var grpId = await PersonIdAsync("HAP-GRP-01");
        var pfId = await PersonIdAsync("HAP-PF-01");

        // Each (role caller, out-of-reach subject) → 404 with no audit trace. Covers all seven roles:
        // Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive, Platform Admin.
        var attempts = new (string Caller, Guid Subject, string Why)[]
        {
            ("HAP-SEED-IND", grpId, "Individual reading up the chain"),
            ("MGR_S", seedIndId, "Manager reading a report of a different manager"),
            ("HAP-BUL-01", pfId, "BU Lead reading up the chain (PF-01 has no reviewer)"),
            ("HAP-GRP-01", seedIndId, "Group Leader reading a 2-hop descendant (not reviewer of record)"),
            ("HAP-PF-01", seedIndId, "Portfolio Leader reading a 2+-hop descendant (transitive closed)"),
            ("EXEC", seedIndId, "HIG Executive — no individual-read capability (FR-025 §2)"),
            ("ADMIN", seedIndId, "Platform Admin — no individual-read capability"),
        };

        foreach (var (caller, subject, why) in attempts)
        {
            var before = await IndividualViewCountAsync(subject);
            var client = await ClientAsync(caller);
            var response = await ReadMemberAsync(client, subject);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{why}: expected 404, got {response.StatusCode}");
            Assert.Equal(before, await IndividualViewCountAsync(subject)); // denied read leaves NO audit row
        }
    }

    // === DR-0005 one-hop ALLOW + audited; 2+-hop DENY ============================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task DR0005_one_hop_direct_read_is_allowed_and_audited_across_tiers()
    {
        await SeedAsync();
        var grpId = await PersonIdAsync("HAP-GRP-01");
        var bulId = await PersonIdAsync("HAP-BUL-01");
        var pfId = await PersonIdAsync("HAP-PF-01");

        // HAP-PF-01 → HAP-GRP-01 (one hop, ratified ALLOW) — allowed and writes exactly one IndividualView.
        var pf = await ClientAsync("HAP-PF-01");
        Assert.Equal(HttpStatusCode.OK, (await ReadMemberAsync(pf, grpId)).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(grpId));
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var row = await db.AuditLogs.SingleAsync(a => a.Action == AuditAction.IndividualView && a.SubjectPersonId == grpId);
            Assert.Equal(pfId, row.ActorPersonId);
        }

        // HAP-GRP-01 → HAP-BUL-01 (one hop, ratified ALLOW).
        var grp = await ClientAsync("HAP-GRP-01");
        Assert.Equal(HttpStatusCode.OK, (await ReadMemberAsync(grp, bulId)).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(bulId));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task DR0005_two_plus_hop_read_is_denied_but_the_reviewer_of_record_is_allowed()
    {
        await SeedAsync();
        var seedIndId = await PersonIdAsync("HAP-SEED-IND");

        // HAP-PF-01 → HAP-SEED-IND (3 hops) → DENIED (transitive closed), no audit.
        var pf = await ClientAsync("HAP-PF-01");
        Assert.Equal(HttpStatusCode.NotFound, (await ReadMemberAsync(pf, seedIndId)).StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(seedIndId));

        // HAP-BUL-01 IS SEED-IND's reviewer of record (direct manager) → ALLOWED + audited.
        var bul = await ClientAsync("HAP-BUL-01");
        Assert.Equal(HttpStatusCode.OK, (await ReadMemberAsync(bul, seedIndId)).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(seedIndId));
    }

    // === DR-0006 contractor manager DENY ==========================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task DR0006_contractor_manager_cannot_read_its_report_which_escalates_to_the_employee_ancestor()
    {
        await SeedAsync();
        var empcId = await PersonIdAsync("EMP-C");

        // The contractor direct manager CTR-01 → EMP-C: DENIED, no audit (contractor gets no individual access).
        var ctr = await ClientAsync("CTR-01");
        Assert.Equal(HttpStatusCode.NotFound, (await ReadMemberAsync(ctr, empcId)).StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(empcId));

        // The employee reviewer of record HAP-BUL-01 (contractor skipped) → ALLOWED + audited.
        var bul = await ClientAsync("HAP-BUL-01");
        Assert.Equal(HttpStatusCode.OK, (await ReadMemberAsync(bul, empcId)).StatusCode);
        Assert.Equal(1, await IndividualViewCountAsync(empcId));
    }

    // === Above-BU aggregates-only (FR-025 §2) =====================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task HIG_Executive_sees_aggregates_only_never_an_individual_score()
    {
        await SeedAsync();
        var seedIndId = await PersonIdAsync("HAP-SEED-IND");
        var exec = await ClientAsync("EXEC");

        // No individual read at all (FR-025 §2) — 404, no audit.
        Assert.Equal(HttpStatusCode.NotFound, (await ReadMemberAsync(exec, seedIndId)).StatusCode);
        Assert.Equal(0, await IndividualViewCountAsync(seedIndId));

        // But the all-HIG aggregate rollup IS visible.
        var rollup = await exec.GetAsync("/api/org/allhig/rollup");
        Assert.Equal(HttpStatusCode.OK, rollup.StatusCode);
        var node = (await rollup.Content.ReadFromJsonAsync<NodeDto>())!;
        Assert.Equal("AllHig", node.NodeType);
    }

    // === N<4 suppression end-to-end (SC-006) ======================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task A_sub_four_team_aggregate_is_suppressed_with_no_figures()
    {
        await SeedAsync();
        // MGR_S's team is exactly S1..S3 (3 scored people) → N<4 → suppressed, no figures leak.
        var mgrS = await ClientAsync("MGR_S");
        var response = await mgrS.GetAsync("/api/me/team/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = (await response.Content.ReadFromJsonAsync<NodeDto>())!;
        Assert.True(node.Suppressed);
        Assert.Null(node.Figures);           // FR-071 — a suppressed aggregate emits NO number
        Assert.Equal("N<4", node.SuppressionReason);
    }

    // === Audit tie-in (V3 step 4) =================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task After_an_allowed_view_the_platform_admin_audit_search_surfaces_the_individual_view_row()
    {
        var admin = await SeedAsync();
        var seedIndId = await PersonIdAsync("HAP-SEED-IND");
        var bulId = await PersonIdAsync("HAP-BUL-01");

        var bul = await ClientAsync("HAP-BUL-01");
        Assert.Equal(HttpStatusCode.OK, (await ReadMemberAsync(bul, seedIndId)).StatusCode);

        var rows = (await (await admin.GetAsync($"/api/admin/audit?subject={seedIndId}&action=IndividualView"))
            .Content.ReadFromJsonAsync<List<AuditRowView>>())!;
        var row = Assert.Single(rows);
        Assert.Equal(bulId, row.ActorPersonId);
        Assert.Equal(seedIndId, row.SubjectPersonId);
        Assert.Equal(nameof(AuditAction.IndividualView), row.Action);
    }
}
