using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Hap.Api.Authorization;
using Hap.Domain.Assessments;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-12 FINAL re-QA (fresh instance, CLAUDE.md §9) after the structural erasure-disclosure fix
/// (commits b17fbbe/fb54643). No shared context with Dev or the prior QA pass — these tests independently
/// re-attack the class of defect the prior QA found (a raw-score read presenting retention-erased data as
/// genuine), from angles the existing suite does not cover: a DIFFERENT authorised-reader shape (a DR-0005
/// hierarchy direct read, not the plain-Manager path the reproducing test used), and a REFLECTION-based
/// completeness cross-check of the <c>SeamBoundaryTests</c> text-scan guard against the actual interface
/// surface (an independent technique from the regex the guard itself uses, so the two can't share a blind
/// spot).
/// </summary>
[Collection("hap-db")]
public sealed class QaAdversarialHap12FinalTests
{
    private readonly HapApiFactory _factory;

    public QaAdversarialHap12FinalTests(HapApiFactory factory) => _factory = factory;

    // === Attack 1: does the member-read erasure refusal generalise past the plain-Manager reach, or is it
    // only reachable/tested through DirectReports-via-plain-manager? Exercise it through a DR-0005 one-hop
    // hierarchy direct read instead (a Group Leader reading their immediate direct report, a BU Lead) — a
    // genuinely different authorisation path through AssessmentReads.AuthorizeIndividualRead, still landing
    // on the same ErasureLedger-guarded GetMemberAssessmentAsync. =================================

    private async Task<HttpClient> SeedHierarchyDormantErasedAsync()
    {
        await _factory.ResetAsync();
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_PF"),
            Snap.Person("HAP-GRP-01", "BU_PF"),                                     // Group Leader, no manager
            Snap.Person("HAP-BUL-01", "BU_PF", managerExternalRef: "HAP-GRP-01"),   // one hop below GRP (DR-0005)
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
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("HAP-GRP-01", role: "Group Leader", buCode: "BU_PF"),
            Snap.SeedUser("HAP-BUL-01", role: "BU Lead", buCode: "BU_PF"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(seed!.VersionId, "2023-01", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null)).StatusCode);

        // HAP-BUL-01 self-scores and is moderated by their reviewer of record, HAP-GRP-01 (the DR-0005
        // one-hop direct read is also the moderation path — moderation ⊆ read).
        var bul = await ClientAsync("HAP-BUL-01");
        var form = (await (await bul.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await bul.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, 3, "genuinely sensitive")).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await bul.PostAsync("/api/me/assessment/submit", null)).StatusCode);

        var bulId = await PersonIdAsync("HAP-BUL-01");
        var grp = await ClientAsync("HAP-GRP-01");
        var view = (await (await grp.GetAsync($"/api/team/members/{bulId}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, 1, "moderated to L1")).ToArray();
        Assert.Equal(HttpStatusCode.NoContent,
            (await grp.PutAsJsonAsync($"/api/team/reviews/{view.AssessmentId}", new ModerateReviewRequest(decisions))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/cycles/{cycle.Id}/close", null)).StatusCode);
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE cycles SET \"ClosesAt\" = {DateTime.UtcNow.AddYears(-4)} WHERE \"Id\" = {cycle.Id}");
        }
        var retention = await admin.PostAsync("/api/admin/retention/run", null);
        var retentionResult = (await retention.Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.True(retentionResult.AssessmentsErased >= 1, "setup invariant: HAP-BUL-01's assessment must actually be erased");

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

    /// <summary>The dormant-platform erasure refusal must hold for EVERY authorised-reader shape, not just
    /// the plain-Manager DirectReports path the reproducing test (QaAdversarialHap12Tests) exercised. Here
    /// the reader is a DR-0005 one-hop hierarchy direct reader (Group Leader → their BU Lead direct
    /// report) — a distinct branch through <c>AssessmentReads.AuthorizeIndividualRead</c>/<c>ClassifyReader</c>.
    /// If the ledger check in <c>GetMemberAssessmentAsync</c> were ever made conditional on reach type
    /// (a plausible future "optimisation"), this is what would catch it.</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Dormant_erasure_refusal_holds_for_a_DR0005_hierarchy_direct_reader_not_only_a_plain_manager()
    {
        await SeedHierarchyDormantErasedAsync();
        var bulId = await PersonIdAsync("HAP-BUL-01");

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var auditRowsBefore = await db.AuditLogs.CountAsync(a =>
            a.Action == Hap.Domain.Audit.AuditAction.IndividualView && a.SubjectPersonId == bulId);

        // No new cycle ever opens — CurrentCycleAsync falls through to the erased closed cycle. HAP-GRP-01
        // is still HAP-BUL-01's reviewer of record and still has DR-0005 read capability; the ONLY thing
        // that changed is the assessment is now erased.
        var grp = await ClientAsync("HAP-GRP-01");
        var response = await grp.GetAsync($"/api/team/members/{bulId}/assessment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var auditRowsAfter = await db.AuditLogs.CountAsync(a =>
            a.Action == Hap.Domain.Audit.AuditAction.IndividualView && a.SubjectPersonId == bulId);
        // Exactly the one row from the PRE-erasure moderation view — the refused post-erasure read added none.
        Assert.Equal(1, auditRowsBefore);
        Assert.Equal(auditRowsBefore, auditRowsAfter);
    }

    // === Attack 2: independent reflection-based completeness cross-check of the erasure-display guard.
    // SeamBoundaryTests.DisplayReadMethods is a hand-maintained regex over method NAMES (the exact thing
    // that was incomplete in panel round 1 — 2 methods were missing). A text-scan regex and a reflection
    // scan over the actual interface share no code, so this closes the same blind spot from a second,
    // independent angle: if a NEW method returning raw AssessmentScore data is ever added to
    // IAssessmentStore/ISelfAssessmentStore/AssessmentReads and nobody remembers to extend the guard's
    // regex, THIS test fails even though the text-scan guard (which only sees what IS already in its list)
    // would not notice the gap in its own coverage. =================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Every_score_bearing_store_or_gateway_method_is_named_in_the_erasure_display_guard()
    {
        // Mirrors SeamBoundaryTests.DisplayReadMethods (Hap.Architecture.Tests) — duplicated deliberately
        // (that project does not reference Hap.Api) so this check is genuinely independent, not a shared
        // fixture the two tests could both silently drift out of sync with together. If this list and the
        // guard's regex diverge, BOTH this test's own assertion below AND (separately) the architecture
        // guard should be updated — a mismatch between them is itself worth investigating.
        var guardedMethodNames = new HashSet<string>
        {
            "GetAssessmentWithScoresAsync", "GetByIdWithScoresAsync", "GetSelfAsync",
            "GetSelfScoresForCycleAsync", "GetIndividualScoresAsync", "ReadIndividualScoresAsync",
            "GetAllForPersonAsync",
        };

        static bool ReturnsRawScores(Type returnType)
        {
            var t = returnType;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                t = t.GetGenericArguments()[0];
            }
            var inspected = t.IsGenericType ? t.GetGenericArguments()[0] : t; // unwrap IReadOnlyList<T>/Nullable<T>
            var name = inspected.Name;
            return name is nameof(AssessmentScore) or nameof(AssessmentWithScores);
        }

        var candidates = typeof(IAssessmentStore).GetMethods()
            .Concat(typeof(ISelfAssessmentStore).GetMethods())
            .Concat(typeof(AssessmentReads).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => ReturnsRawScores(m.ReturnType))
            .Select(m => m.Name)
            .Distinct()
            .ToList();

        // Canary: the reflection scan itself must find something, or this proves nothing (a broken/vacuous
        // scan would pass trivially against an empty candidate set).
        Assert.NotEmpty(candidates);

        var uncovered = candidates.Where(name => !guardedMethodNames.Contains(name)).ToList();
        Assert.True(uncovered.Count == 0,
            "These score-bearing IAssessmentStore/ISelfAssessmentStore/AssessmentReads methods are not named " +
            "in the erasure-display guard's method list — extend SeamBoundaryTests.DisplayReadMethods (and this " +
            "list) or the structural guard is not actually exhaustive: " + string.Join(", ", uncovered));

        // And the reverse: nothing in the guard's list should be a name that no longer exists on the
        // interfaces (a stale entry hides a rename that silently dropped coverage of the real method).
        var stale = guardedMethodNames.Where(name => !candidates.Contains(name)).ToList();
        Assert.True(stale.Count == 0,
            "These names in the guard's method list no longer match any score-bearing store/gateway method — " +
            "likely a rename that silently dropped real coverage: " + string.Join(", ", stale));
    }
}
