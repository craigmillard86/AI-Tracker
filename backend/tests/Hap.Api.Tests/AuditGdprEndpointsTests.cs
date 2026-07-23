using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// HAP-12 acceptance criteria for the audit &amp; GDPR surfaces (contracts/api.md; FR-050/051/052/053):
/// the right-of-access export (GET /api/me/export) validated against a hand-assembled expected export and
/// its fail-closed Export audit row; the read-only admin audit search (GET /api/admin/audit — filtered,
/// Platform-Admin only); and the retention job (POST /api/admin/retention/run — nulls raw values older than
/// 3 years, retains rows, leaves snapshots untouched, one RetentionErasure row per assessment, idempotent).
///
/// <para>Fixture: BU01 onboarded; ADMIN (Platform Admin), MGR1 (manager of EMP1/EMP2), EMP1/EMP2
/// (Individuals). The framework seeds seven dimensions with 0–3 descriptors.</para>
/// </summary>
[Collection("hap-db")]
public sealed class AuditGdprEndpointsTests
{
    private readonly HapApiFactory _factory;

    public AuditGdprEndpointsTests(HapApiFactory factory) => _factory = factory;

    // --- setup helpers ---------------------------------------------------------------------------

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
                Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var bu01 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU01");
            bu01.SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("EMP2", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        return (admin, seed!.VersionId);
    }

    private static async Task<Guid> CreateAndOpenCycleAsync(HttpClient admin, Guid frameworkVersionId, string name = "2026-08")
    {
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(frameworkVersionId, name, true));
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

    /// <summary>Signs in as <paramref name="externalRef"/>, self-scores every dimension with
    /// <paramref name="score"/> and evidence <paramref name="evidence"/>, and submits.</summary>
    private async Task SubmitSelfAsync(string externalRef, int score, string? evidence)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, evidence)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    /// <summary>MGR1 moderates EMP1's current assessment: every dimension to <paramref name="managerScore"/>
    /// with <paramref name="comment"/> (needed when the divergence is ≥2).</summary>
    private async Task<Guid> ModerateEmp1Async(string emp1Ref, int managerScore, string comment)
    {
        var emp1Id = await PersonIdAsync(emp1Ref);
        var mgr1 = await ClientAsync("MGR1");
        var view = (await (await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment"))
            .Content.ReadFromJsonAsync<MemberAssessmentResponse>())!;
        var decisions = view.Dimensions.Select(d => new ModerateDecision(d.DimensionId, managerScore, comment)).ToArray();
        Assert.Equal(HttpStatusCode.NoContent,
            (await mgr1.PutAsJsonAsync($"/api/team/reviews/{view.AssessmentId}", new ModerateReviewRequest(decisions))).StatusCode);
        return view.AssessmentId;
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<Guid> LatestAssessmentIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        return await db.Set<Assessment>()
            .Where(a => a.PersonId == personId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Id)
            .FirstAsync();
    }

    /// <summary>Back-dates a cycle's close to <paramref name="years"/> years ago, so the retention job sees
    /// it as past the 3-year window. Raw SQL because <c>Cycle.ClosesAt</c> is domain-immutable (private set).</summary>
    private async Task BackdateCloseAsync(Guid cycleId, int years)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var old = DateTime.UtcNow.AddYears(-years);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cycles SET \"ClosesAt\" = {old} WHERE \"Id\" = {cycleId}");
    }

    /// <summary>Full-content fingerprint of every frozen RollupSnapshot (ordered by id): every figure —
    /// N, per-dimension means, floor distribution, completion base/pct, unmoderated pct, calibration delta,
    /// suppression verdict — serialised to a string. Comparing before/after retention proves the aggregates
    /// are byte-for-byte untouched, not merely that the row count and N are unchanged.</summary>
    private async Task<List<string>> SnapshotFingerprintAsync(HapDbContext db) =>
        (await db.RollupSnapshots.AsNoTracking().OrderBy(s => s.Id).ToListAsync())
        .Select(s => JsonSerializer.Serialize(new
        {
            s.Id,
            s.CycleId,
            s.OrgNodeType,
            s.OrgNodeRef,
            s.N,
            s.PerDimensionMean,
            s.FloorLevelDistribution,
            s.CompletionDenominator,
            s.CompletionPct,
            s.UnmoderatedPct,
            s.CalibrationDelta,
            s.Suppressed,
            s.SuppressionReason,
        }))
        .ToList();

    // === Right-of-access export (FR-051) ==========================================================

    [Fact]
    public async Task Export_returns_the_full_hand_assembled_data_for_one_synth_user()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "my evidence");        // self 3, evidence
        await ModerateEmp1Async("EMP1", 1, "moderated to L1");  // manager 1 (Δ2 → comment)

        var emp1 = await ClientAsync("EMP1");
        var response = await emp1.GetAsync("/api/me/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var export = (await response.Content.ReadFromJsonAsync<PersonalDataExport>())!;

        // Profile + org links — hand-assembled from the fixture.
        Assert.Equal("EMP1", export.Person.ExternalRef);
        Assert.Equal("EMP1", export.Person.DisplayName);
        Assert.Equal("emp1@synth.local", export.Person.Email);
        Assert.Equal("Employee", export.Person.EmployeeType);
        Assert.Equal(await PersonIdAsync("MGR1"), export.Person.ManagerPersonId);
        Assert.True(export.Person.IsActive);
        Assert.NotEqual(default, export.ExportedAt);

        // Exactly one cycle, fully moderated, seven dimensions, every value present.
        var cycle = Assert.Single(export.Cycles);
        Assert.Equal(cycleId, cycle.CycleId);
        Assert.Equal("2026-08", cycle.CycleName);
        Assert.Equal("Moderated", cycle.State);
        Assert.False(cycle.Unmoderated);
        Assert.NotNull(cycle.SubmittedAt);
        Assert.NotNull(cycle.ModeratedAt);
        Assert.Equal(await PersonIdAsync("MGR1"), cycle.ModeratedByPersonId);
        Assert.False(cycle.DataErased); // live, not retention-erased
        Assert.Equal(7, cycle.Scores.Count);
        Assert.All(cycle.Scores, s =>
        {
            Assert.False(s.Erased);
            Assert.Equal(3, s.SelfScore);
            Assert.Equal("my evidence", s.SelfEvidence);
            Assert.Equal(1, s.ManagerScore);
            Assert.Equal("moderated to L1", s.ManagerComment);
            Assert.False(string.IsNullOrEmpty(s.DimensionKey)); // labelled from framework data, not raw ids
        });
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Export_of_a_retention_erased_assessment_discloses_erasure_never_a_fabricated_score()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "sensitive evidence"); // real self-score 3
        await ModerateEmp1Async("EMP1", 1, "moderated to L1");  // real manager-score 1
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        await BackdateCloseAsync(cycleId, 4);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/retention/run", null)).StatusCode);

        var emp1 = await ClientAsync("EMP1");
        var export = (await (await emp1.GetAsync("/api/me/export")).Content.ReadFromJsonAsync<PersonalDataExport>())!;

        var cycle = Assert.Single(export.Cycles);
        Assert.Equal(cycleId, cycle.CycleId);
        Assert.True(cycle.DataErased);            // disclosed as erased…
        Assert.Equal("Moderated", cycle.State);   // …but the cycle + moderation metadata still show (erased ≠ never-happened)
        Assert.NotNull(cycle.ModeratedAt);
        Assert.Equal(7, cycle.Scores.Count);
        Assert.All(cycle.Scores, s =>
        {
            Assert.True(s.Erased);
            Assert.Null(s.SelfScore);       // NEVER a fabricated 0 (B1)
            Assert.Null(s.SelfEvidence);
            Assert.Null(s.ManagerScore);
            Assert.Null(s.ManagerComment);
        });

        // Defensive: the destroyed evidence string never appears anywhere in the export payload.
        var raw = await (await emp1.GetAsync("/api/me/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive evidence", raw);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Export_writes_exactly_one_Export_audit_row_actor_equals_subject()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2, "ev");
        var emp1Id = await PersonIdAsync("EMP1");

        var emp1 = await ClientAsync("EMP1");
        Assert.Equal(HttpStatusCode.OK, (await emp1.GetAsync("/api/me/export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await emp1.GetAsync("/api/me/export")).StatusCode); // second call

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var rows = await db.AuditLogs
            .Where(a => a.Action == AuditAction.Export && a.SubjectPersonId == emp1Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);                              // one per export call
        Assert.All(rows, r => Assert.Equal(emp1Id, r.ActorPersonId)); // self-access: actor == subject
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Export_is_self_only_and_never_returns_another_persons_data()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "emp1 secret evidence"); // EMP1 has data
        // EMP2 has NO assessment. There is no person parameter on the endpoint — a caller only ever exports
        // their own data.
        var emp2 = await ClientAsync("EMP2");
        var export = (await (await emp2.GetAsync("/api/me/export")).Content.ReadFromJsonAsync<PersonalDataExport>())!;

        Assert.Equal("EMP2", export.Person.ExternalRef);
        Assert.Empty(export.Cycles); // EMP2's own (empty) data — never EMP1's
        // Defensive: EMP1's evidence string never appears anywhere in EMP2's export payload.
        var raw = await (await emp2.GetAsync("/api/me/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("emp1 secret evidence", raw);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Export_is_fail_closed_when_the_audit_write_fails()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2, "ev");
        var emp1Id = await PersonIdAsync("EMP1");

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var svc = new PersonalDataExportService(
                db,
                scope.ServiceProvider.GetRequiredService<ISelfAssessmentStore>(),
                new ThrowingAuditWriter(),
                new ErasureLedger(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportAsync(emp1Id));
        }

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.Export && a.SubjectPersonId == emp1Id));
        }
    }

    // === Admin audit search (FR-050/053) ==========================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Audit_search_returns_filtered_rows_for_platform_admin()
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "ev");
        var emp1Id = await PersonIdAsync("EMP1");
        var emp2Id = await PersonIdAsync("EMP2");

        // Generate audit: MGR1 views EMP1 (IndividualView) then moderates (ScoreChange).
        await ModerateEmp1Async("EMP1", 3, ""); // no divergence → no comment needed; the GET wrote a view row

        // subject filter → only EMP1's rows.
        var bySubject = (await (await admin.GetAsync($"/api/admin/audit?subject={emp1Id}"))
            .Content.ReadFromJsonAsync<List<AuditRowView>>())!;
        Assert.NotEmpty(bySubject);
        Assert.All(bySubject, r => Assert.Equal(emp1Id, r.SubjectPersonId));
        Assert.Contains(bySubject, r => r.Action == nameof(AuditAction.IndividualView));
        Assert.Contains(bySubject, r => r.Action == nameof(AuditAction.ScoreChange));

        // action filter → only IndividualView rows.
        var views = (await (await admin.GetAsync($"/api/admin/audit?action=IndividualView"))
            .Content.ReadFromJsonAsync<List<AuditRowView>>())!;
        Assert.NotEmpty(views);
        Assert.All(views, r => Assert.Equal(nameof(AuditAction.IndividualView), r.Action));

        // from filter in the future → empty.
        var future = DateTime.UtcNow.AddDays(1).ToString("O");
        var none = (await (await admin.GetAsync($"/api/admin/audit?from={Uri.EscapeDataString(future)}"))
            .Content.ReadFromJsonAsync<List<AuditRowView>>())!;
        Assert.Empty(none);

        // EMP2 (no activity) → no rows.
        var emp2Rows = (await (await admin.GetAsync($"/api/admin/audit?subject={emp2Id}"))
            .Content.ReadFromJsonAsync<List<AuditRowView>>())!;
        Assert.Empty(emp2Rows);
    }

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData("EMP1")]
    [InlineData("MGR1")]
    public async Task Audit_search_is_platform_admin_only(string nonAdmin)
    {
        var (admin, fvId) = await SeedAsync();
        await CreateAndOpenCycleAsync(admin, fvId);

        var client = await ClientAsync(nonAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/audit")).StatusCode);
    }

    [Fact]
    public async Task Audit_search_rejects_an_unknown_action_with_400()
    {
        var (admin, _) = await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/audit?action=Nonsense")).StatusCode);
    }

    // === RT(a): the audit log is never a score surface ============================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task No_audit_row_detail_ever_carries_a_score_value_across_every_action()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "evidence");
        var emp1Id = await PersonIdAsync("EMP1");

        // Generate EVERY audited action that carries a Detail: OrgOverride (admin override) + IndividualView
        // (member read) + ScoreChange (moderation) + Export + RetentionErasure.
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/overrides",
            new CreateOverrideRequest(emp1Id, "DottedLine", "MGR1", "audit-detail test", "ADMIN"))).StatusCode); // OrgOverride
        var mgr1 = await ClientAsync("MGR1");
        await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment");     // IndividualView
        await ModerateEmp1Async("EMP1", 1, "moderated to L1");             // ScoreChange
        await (await ClientAsync("EMP1")).GetAsync("/api/me/export");      // Export
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        await BackdateCloseAsync(cycleId, 4);
        await admin.PostAsync("/api/admin/retention/run", null);           // RetentionErasure

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var details = await db.AuditLogs.Select(a => new { a.Action, a.Detail }).ToListAsync();
        // Sanity: every Detail-bearing action actually fired, so the guard below is genuinely exhaustive.
        Assert.Contains(details, d => d.Action == AuditAction.OrgOverride);
        Assert.Contains(details, d => d.Action == AuditAction.IndividualView);
        Assert.Contains(details, d => d.Action == AuditAction.ScoreChange);
        Assert.Contains(details, d => d.Action == AuditAction.Export);
        Assert.Contains(details, d => d.Action == AuditAction.RetentionErasure);

        // A score VALUE would surface as a property keyed self/manager-score (a count like "scoreRows" is
        // fine). Assert no audit Detail carries any such key — the audit-reader-is-not-a-score-surface claim,
        // enforced not asserted in prose.
        foreach (var d in details)
        {
            Assert.False(DetailHasScoreValueKey(d.Detail),
                $"audit Detail for {d.Action} must never carry a score value key: {d.Detail}");
        }
    }

    /// <summary>Keys that contain "score" but are COUNTS, not score values — deliberately allowed. Compared
    /// after lower-casing and stripping underscores. Extend only for another genuine count/quantity key.</summary>
    private static readonly HashSet<string> CountStyleScoreKeys = new() { "scorerows" };

    /// <summary>True if the JSON detail has any property (at any depth) whose name contains "score" and is
    /// not an allow-listed count key — i.e. a potential score-VALUE leak. Count-style keys such as
    /// "scoreRows" are deliberately allowed.</summary>
    private static bool DetailHasScoreValueKey(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }
        using var doc = JsonDocument.Parse(detail);
        return HasScoreKey(doc.RootElement);

        static bool HasScoreKey(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        var name = prop.Name.Replace("_", "").ToLowerInvariant();
                        // ANY key containing "score" is treated as a potential score-value leak (catches a
                        // future "scores"/"scoreValue"/"selfScore" etc.), except explicit count-style keys.
                        if (name.Contains("score") && !CountStyleScoreKeys.Contains(name))
                        {
                            return true;
                        }
                        if (HasScoreKey(prop.Value))
                        {
                            return true;
                        }
                    }
                    return false;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                    {
                        if (HasScoreKey(item))
                        {
                            return true;
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }
    }

    // === Retention (FR-052) =======================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Retention_nulls_old_raw_values_retains_rows_and_leaves_snapshots_untouched()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "sensitive evidence");
        var assessmentId = await ModerateEmp1Async("EMP1", 1, "moderated to L1");
        var emp1Id = await PersonIdAsync("EMP1");
        var adminId = await PersonIdAsync("ADMIN");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null); // freezes snapshots + auto-adopts non-moderated

        // Capture the FULL frozen snapshot payload BEFORE retention (every figure, not just Id+N) so a bug
        // that mutated any snapshot figure during erasure would be caught.
        List<string> snapsBefore;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            snapsBefore = await SnapshotFingerprintAsync(db);
        }
        Assert.NotEmpty(snapsBefore);

        await BackdateCloseAsync(cycleId, 4); // now 4 years old → past the 3-year window

        var runResponse = await admin.PostAsync("/api/admin/retention/run", null);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var result = (await runResponse.Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.True(result.AssessmentsErased >= 1, "EMP1's assessment must be erased");
        Assert.Equal(7, result.ScoreRowsErased);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

            // Rows RETAINED — the assessment and its seven score rows still exist.
            Assert.True(await db.Set<Assessment>().AnyAsync(a => a.Id == assessmentId));
            var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentId).ToListAsync();
            Assert.Equal(7, scores.Count);
            // Raw VALUES nulled (self-score zeroed per Q-027; nullable fields null).
            Assert.All(scores, s =>
            {
                Assert.Equal(0, s.SelfScore);
                Assert.Null(s.SelfEvidence);
                Assert.Null(s.ManagerScore);
                Assert.Null(s.ManagerComment);
            });

            // Snapshots UNTOUCHED — every figure byte-identical before/after (the sacred "aggregates
            // untouched by erasure" proof, not just Id+N).
            var snapsAfter = await SnapshotFingerprintAsync(db);
            Assert.Equal(snapsBefore, snapsAfter);

            // Exactly one RetentionErasure audit row for EMP1's assessment, actor == the running admin.
            var erasureRows = await db.AuditLogs
                .Where(a => a.Action == AuditAction.RetentionErasure && a.SubjectPersonId == emp1Id)
                .ToListAsync();
            Assert.Single(erasureRows);
            Assert.Equal(adminId, erasureRows[0].ActorPersonId);
        }
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Retention_is_idempotent_a_second_run_erases_nothing_and_writes_no_new_audit_rows()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 2, "ev");
        await ModerateEmp1Async("EMP1", 2, "");
        var emp1Id = await PersonIdAsync("EMP1");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        await BackdateCloseAsync(cycleId, 5);

        var first = (await (await admin.PostAsync("/api/admin/retention/run", null))
            .Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.True(first.AssessmentsErased >= 1);

        var second = (await (await admin.PostAsync("/api/admin/retention/run", null))
            .Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.Equal(0, second.AssessmentsErased);
        Assert.Equal(0, second.ScoreRowsErased);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        // Still exactly one RetentionErasure row for the assessment — the second run wrote none.
        Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.RetentionErasure && a.SubjectPersonId == emp1Id));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Retention_leaves_within_window_cycles_untouched()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "recent evidence");
        var assessmentId = await ModerateEmp1Async("EMP1", 1, "moderated to L1");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null); // closed just now — within the 3-year window

        var result = (await (await admin.PostAsync("/api/admin/retention/run", null))
            .Content.ReadFromJsonAsync<RetentionRunResponse>())!;
        Assert.Equal(0, result.AssessmentsErased);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentId).ToListAsync();
        Assert.All(scores, s =>
        {
            Assert.Equal(3, s.SelfScore);              // untouched
            Assert.Equal("recent evidence", s.SelfEvidence);
            Assert.Equal(1, s.ManagerScore);
        });
    }

    // === Erasure permanence — the late-override write path cannot reverse an erasure =================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Late_override_re_moderation_of_a_retention_erased_assessment_is_refused_and_erasure_stands()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP1", 3, "sensitive evidence"); // Submitted, NOT moderated
        var emp1Id = await PersonIdAsync("EMP1");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null); // close auto-adopts EMP1 → AutoAdopted
        await BackdateCloseAsync(cycleId, 4);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/retention/run", null)).StatusCode);
        var assessmentId = await LatestAssessmentIdAsync("EMP1");

        // An admin grants a late override on the >3y-erased cycle (Q-022 has no age restriction) and MGR1
        // attempts to re-moderate — this MUST be refused (409), not silently reverse the erasure.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id))).StatusCode);
        var mgr1 = await ClientAsync("MGR1");
        var moderate = await mgr1.PutAsJsonAsync($"/api/team/reviews/{assessmentId}",
            new ModerateReviewRequest(Array.Empty<ModerateDecision>()));
        Assert.Equal(HttpStatusCode.Conflict, moderate.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        // Erasure NOT reversed — EVERY raw value is still erased and no ScoreChange row was written.
        var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentId).ToListAsync();
        Assert.All(scores, s =>
        {
            Assert.Equal(0, s.SelfScore);
            Assert.Null(s.SelfEvidence);
            Assert.Null(s.ManagerScore);
            Assert.Null(s.ManagerComment);
        });
        Assert.Equal(0, await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.ScoreChange && a.SubjectPersonId == emp1Id));

        // The export STILL truthfully reports the data erased (no genuine value slipped back in behind the
        // permanent ledger).
        var export = (await (await (await ClientAsync("EMP1")).GetAsync("/api/me/export"))
            .Content.ReadFromJsonAsync<PersonalDataExport>())!;
        Assert.True(Assert.Single(export.Cycles).DataErased);
    }

    // === Erasure disclosure/refusal across the OTHER raw-score read surfaces (QA finding) ===========

    /// <summary>Sets up a single erased cycle on a now-dormant platform (no cycle opened since), returning
    /// the erased cycle id and EMP1's id. EMP1 self-scores 3 with evidence, MGR1 moderates to 1, the cycle
    /// closes, is back-dated &gt;3y, and retention erases it — after which SeamCycleResolver resolves this
    /// erased closed cycle as "current".</summary>
    private async Task<(HttpClient Admin, Guid FrameworkVersionId, Guid CycleId, Guid Emp1Id)> SeedDormantErasedAsync()
    {
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId, "2023-01");
        await SubmitSelfAsync("EMP1", 3, "genuinely sensitive evidence");
        await ModerateEmp1Async("EMP1", 1, "moderated to L1");
        var emp1Id = await PersonIdAsync("EMP1");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null);
        await BackdateCloseAsync(cycleId, 4);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/retention/run", null)).StatusCode);
        return (admin, fvId, cycleId, emp1Id);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Member_read_of_a_retention_erased_assessment_is_refused_404_with_no_audit()
    {
        var (_, _, _, emp1Id) = await SeedDormantErasedAsync();

        // The manager (not the data subject) must not be served the fabricated placeholder — the erased
        // assessment is refused (404, existence-leak) and writes NO IndividualView audit row.
        var mgr1 = await ClientAsync("MGR1");
        var response = await mgr1.GetAsync($"/api/team/members/{emp1Id}/assessment");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        // Exactly one IndividualView row exists from the PRE-erasure moderation view; the post-erasure read
        // added none (a refused read is not a view).
        Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.IndividualView && a.SubjectPersonId == emp1Id));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Result_view_of_a_retention_erased_assessment_discloses_erasure_to_the_subject()
    {
        await SeedDormantErasedAsync();

        // The data subject's OWN result view must DISCLOSE erasure (right-of-access), never present the
        // placeholder as a genuine moderated result.
        var emp1 = await ClientAsync("EMP1");
        var response = await emp1.GetAsync("/api/me/assessment/result");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AssessmentResultResponse>())!;
        Assert.True(result.DataErased);
        Assert.NotEmpty(result.Dimensions);
        Assert.All(result.Dimensions, d =>
        {
            Assert.True(d.Erased);
            Assert.Null(d.ManagerComment); // the genuine "moderated to L1" comment was destroyed
        });
        var raw = await (await emp1.GetAsync("/api/me/assessment/result")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("moderated to L1", raw); // destroyed manager comment never resurfaces
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Self_form_does_not_prefill_from_a_retention_erased_prior_cycle()
    {
        var (admin, fvId, _, _) = await SeedDormantErasedAsync(); // cycle 2023-01 now erased

        // A NEW cycle opens (platform no longer dormant). The self-form's FR-062 pre-fill must NOT seed a
        // fabricated 0 from the erased prior cycle — prior is disclosed-as-absent (priorScore null).
        await CreateAndOpenCycleAsync(admin, fvId, "2026-08");

        var emp1 = await ClientAsync("EMP1");
        var form = (await (await emp1.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        Assert.False(form.DataErased);                              // the current (new) cycle is not erased
        Assert.All(form.Dimensions, d => Assert.Null(d.PriorScore)); // …but the erased prior never pre-fills
    }

    [Fact]
    public async Task A_normal_post_close_late_override_re_moderation_still_succeeds()
    {
        // Guard against over-restricting the Q-022 path: a late override on a NON-erased closed cycle must
        // still allow re-moderation.
        var (admin, fvId) = await SeedAsync();
        var cycleId = await CreateAndOpenCycleAsync(admin, fvId);
        await SubmitSelfAsync("EMP2", 2, "ev"); // Submitted, not moderated
        var emp2Id = await PersonIdAsync("EMP2");
        await admin.PostAsync($"/api/cycles/{cycleId}/close", null); // auto-adopts EMP2 (no retention run)
        var assessmentId = await LatestAssessmentIdAsync("EMP2");

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp2Id))).StatusCode);
        var mgr1 = await ClientAsync("MGR1");
        var moderate = await mgr1.PutAsJsonAsync($"/api/team/reviews/{assessmentId}",
            new ModerateReviewRequest(Array.Empty<ModerateDecision>()));
        Assert.Equal(HttpStatusCode.NoContent, moderate.StatusCode);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Self_write_to_a_retention_erased_assessment_is_refused_and_erasure_stands()
    {
        var (admin, fvId, cycleId, emp1Id) = await SeedDormantErasedAsync(); // cycle now erased, still "current"

        // A late override reopens the erased closed cycle for submission; the self-WRITE interlock must then
        // still REFUSE (409) — a Q-022 late override cannot re-populate an erased row (self-inflicted desync).
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id))).StatusCode);
        var emp1 = await ClientAsync("EMP1");
        var form = (await (await emp1.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        var write = await emp1.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, 3, "re-entered")).ToList()));
        Assert.Equal(HttpStatusCode.Conflict, write.StatusCode);
        var submit = await emp1.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var assessmentId = await LatestAssessmentIdAsync("EMP1");
            var scores = await db.Set<AssessmentScore>().Where(s => s.AssessmentId == assessmentId).ToListAsync();
            Assert.All(scores, s => { Assert.Equal(0, s.SelfScore); Assert.Null(s.SelfEvidence); }); // erasure stands

            // The form itself disclosed the current-cycle erasure (no fabricated pre-fill values).
            Assert.True(form.DataErased);
        }

        // A normal self-submit on a FRESH open cycle still succeeds — the interlock does not over-restrict.
        await CreateAndOpenCycleAsync(admin, fvId, "2026-08");
        var emp2 = await ClientAsync("EMP2");
        var freshForm = (await (await emp2.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await emp2.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(freshForm.Dimensions.Select(d => new ScoreEntry(d.DimensionId, 2, null)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await emp2.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Retention_erasure_audit_detail_round_trips_through_the_ledger_parser()
    {
        // Writer-shape guard: the retention job's RetentionErasure Detail must always parse back to its
        // assessment id (the ledger's fail-closed parse depends on this).
        var (_, _, _, emp1Id) = await SeedDormantErasedAsync();
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var assessmentId = await LatestAssessmentIdAsync("EMP1");
        var detail = await db.AuditLogs
            .Where(a => a.Action == AuditAction.RetentionErasure && a.SubjectPersonId == emp1Id)
            .Select(a => a.Detail)
            .SingleAsync();
        Assert.Contains(assessmentId, ErasureLedger.ParseErasedAssessmentIds(new[] { (string?)detail }));
    }

    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData("not json at all")]
    [InlineData("{}")]                       // valid json, but no assessmentId
    [InlineData("{\"assessmentId\":\"not-a-guid\"}")]
    [InlineData(null)]                       // empty/absent detail
    public void Ledger_parse_fails_closed_on_an_unparseable_retention_erasure_detail(string? detail)
    {
        // A RetentionErasure row we cannot resolve to an assessment id is a corrupt privacy ledger — the
        // parser must THROW (fail closed), never silently drop it (which would fail open on the display reads).
        Assert.Throws<CorruptErasureLedgerException>(() => ErasureLedger.ParseErasedAssessmentIds(new[] { detail }));
    }
}
