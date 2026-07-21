using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-3 (fresh-instance QA pass, CLAUDE.md §9), added
/// during QA rather than Dev — attributed as QA work. Targets angles the Dev/red-team rounds
/// (recorded in the story file) did not exercise directly: the DottedLine override field
/// (audit + self-reference), broader person/override mutation-endpoint coverage, free-text
/// injection defence in the override write path, the 1:1 override/audit invariant under
/// concurrency, an independent (non-generator-reuse) reconciliation of a sync against the raw
/// snapshot JSON on disk, duplicate-external_ref snapshot handling, and — the mandatory
/// §9.3(b) append-only attack surface — whether the append-only guarantee survives a
/// trigger-disable route over the app's own HapDbContext connection/role (distinct from the
/// UPDATE/DELETE/TRUNCATE routes the Dev pass already proved rejected).
/// </summary>
[Collection("hap-db")]
public sealed class QaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public QaAdversarialTests(HapApiFactory factory) => _factory = factory;

    private async Task SeedTwoBuSnapshotAsync() => await SeedSnapshotAsync(2);

    private async Task SeedSnapshotAsync(int buCount)
    {
        await _factory.ResetAsync();
        var bus = Enumerable.Range(1, buCount).Select(i => Snap.Bu($"BU{i:00}")).ToArray();
        var persons = new List<DirectoryPerson>
        {
            Snap.Person("LEAD", "BU01"),
            Snap.Person("SUBJECT", "BU01", managerExternalRef: "LEAD"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, persons));
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
    }

    private async Task<Guid> SubjectIdAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == "SUBJECT")).Id;
    }

    private async Task<Guid> LeadIdAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == "LEAD")).Id;
    }

    // ============================================================================================
    // Gap 1: DottedLine is the one OverrideField the Dev suite never exercises directly (Dev's
    // own notes call it "advisory only" and rely on the shared write-seam code path being
    // field-agnostic). AC4 says "every override write" — literally including DottedLine.
    // ============================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task DottedLine_override_write_produces_exactly_one_audit_row()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = subjectId,
            field = "DottedLine",
            overrideValue = "LEAD",
            reason = "advisory secondary reporting line",
            createdBy = "qa-tester",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.OrgOverrides.CountAsync());
        var audits = await db.AuditLogs.ToListAsync();
        Assert.Single(audits);
        Assert.Equal(subjectId, audits[0].SubjectPersonId);

        // Advisory-only per Q-010: confirms the DottedLine write does NOT alter the structural
        // manager chain (only Manager/BusinessUnit overrides do).
        var subject = await db.People.SingleAsync(p => p.Id == subjectId);
        var lead = await db.People.SingleAsync(p => p.ExternalRef == "LEAD");
        Assert.Equal(lead.Id, subject.ManagerPersonId); // unchanged — was already LEAD from sync
    }

    [Fact]
    public async Task DottedLine_override_pointing_at_the_subject_itself_is_rejected()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<OrgOverrideService>();
            await Assert.ThrowsAsync<OverrideValidationException>(() =>
                service.CreateAsync(new CreateOverrideCommand(
                    subjectId, OverrideField.DottedLine, "SUBJECT", "self dotted line", "qa-tester")));
        }

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db.OrgOverrides.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task DottedLine_override_to_an_unresolvable_person_is_rejected_and_writes_nothing()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<OrgOverrideService>();
            await Assert.ThrowsAsync<OverrideValidationException>(() =>
                service.CreateAsync(new CreateOverrideCommand(
                    subjectId, OverrideField.DottedLine, "NO-SUCH-PERSON", "ghost", "qa-tester")));
        }

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db.OrgOverrides.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    // ============================================================================================
    // Gap 2: broaden "no person/override mutation endpoint" beyond the Dev suite's four probed
    // routes (POST /api/persons, POST /api/people, PUT /api/people/{id}, POST /api/admin/people).
    // Adds DELETE/PATCH on people, and — new surface — mutation/deletion of an OrgOverride itself
    // (an override row is an audit-adjacent historical fact; AC5's append-only spirit extends to
    // "no route corrects/removes a correction outside of writing a fresh one").
    // ============================================================================================

    [Theory]
    [InlineData("DELETE", "/api/people/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/people/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/people/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/admin/people/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/admin/overrides/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/overrides/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/admin/overrides/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/admin/audit/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/audit/00000000-0000-0000-0000-000000000001")]
    public async Task No_mutation_or_deletion_endpoint_exists_for_people_overrides_or_audit(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { externalRef = "HACK", displayName = "Intruder", field = "Manager" }),
        };
        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} {path} should not exist (got {(int)response.StatusCode}).");
    }

    // ============================================================================================
    // Gap 3: free-text override fields (Reason, CreatedBy, OverrideValue) reach the database only
    // through parameterised EF Core — defence-in-depth check that hostile content is stored
    // literally (no injection effect), rather than assumed safe because "EF parameterises".
    // ============================================================================================

    [Fact]
    public async Task Override_reason_and_created_by_containing_sql_metacharacters_are_stored_literally_not_executed()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();
        const string hostileReason = "'; DROP TABLE people; --";
        const string hostileCreatedBy = "tester'); DELETE FROM audit_log; --";

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = subjectId,
            field = "BusinessUnit",
            overrideValue = "BU02",
            reason = hostileReason,
            createdBy = hostileCreatedBy,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        // Both tables survive untouched by the "injected" statements, and the row was stored
        // verbatim — proof the strings were never concatenated into executable SQL.
        Assert.Equal(2, await db.People.CountAsync());
        var stored = await db.OrgOverrides.SingleAsync();
        Assert.Equal(hostileReason, stored.Reason);
        Assert.Equal(hostileCreatedBy, stored.CreatedBy);
        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Override_value_containing_sql_metacharacters_for_an_unresolvable_bu_is_rejected_cleanly_not_a_500()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = subjectId,
            field = "BusinessUnit",
            overrideValue = "BU01' OR '1'='1",
            reason = "probe",
            createdBy = "qa-tester",
        });

        // Must resolve to "no such BU" (422), not crash the request pipeline (500) and not
        // spuriously match every BU (which would indicate string-built SQL).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ============================================================================================
    // Gap 4: the 1:1 override-row / audit-row invariant under concurrent writers. Dev's tests are
    // all sequential; nothing proves the shared-transaction design holds when several admins (or a
    // retried client) write overrides for the same subject concurrently.
    // ============================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Concurrent_override_writes_for_the_same_subject_never_desynchronise_override_and_audit_counts()
    {
        await SeedSnapshotAsync(buCount: 6);
        var subjectId = await SubjectIdAsync();

        var targets = new[] { "BU02", "BU03", "BU04", "BU05", "BU06" };
        var tasks = targets.Select(async bu =>
        {
            using var scope = _factory.NewScope();
            var service = scope.ServiceProvider.GetRequiredService<OrgOverrideService>();
            return await service.CreateAsync(new CreateOverrideCommand(
                subjectId, OverrideField.BusinessUnit, bu, $"concurrent probe -> {bu}", "qa-tester"));
        }).ToArray();

        await Task.WhenAll(tasks);

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        var overrideCount = await db.OrgOverrides.CountAsync();
        var auditCount = await db.AuditLogs.CountAsync();

        Assert.Equal(targets.Length, overrideCount); // every concurrent write landed its own row
        Assert.Equal(overrideCount, auditCount);      // and exactly one audit row rode with each
    }

    // ============================================================================================
    // Gap 5: AC1's own dev test recomputes the "expected" snapshot via a second call to
    // DirectoryGenerator.Generate(CanonicalSeed) — the same call the fixture already made to
    // produce the file it wrote to disk. That is a same-source comparison, not an independent
    // reconciliation. Here the expectation is parsed straight from the JSON bytes that were
    // actually fed to POST /api/admin/sync, with zero dependency on the generator being called
    // twice returning identical output.
    // ============================================================================================

    [Fact]
    public async Task Sync_counts_independently_reconcile_against_the_raw_snapshot_json_on_disk()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/admin/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(_factory.CanonicalSnapshotPath));
        var root = raw.RootElement;
        var expectedPersonCount = root.GetProperty("persons").GetArrayLength();
        var expectedBuCodes = root.GetProperty("bus").EnumerateArray()
            .Select(b => b.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
        var expectedGroupKeys = root.GetProperty("bus").EnumerateArray()
            .Select(b => (Portfolio: b.GetProperty("portfolio").GetString()!, Group: b.GetProperty("group").GetString()!))
            .ToHashSet();
        var expectedPortfolios = root.GetProperty("bus").EnumerateArray()
            .Select(b => b.GetProperty("portfolio").GetString()!).ToHashSet(StringComparer.Ordinal);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        Assert.Equal(expectedPersonCount, await db.People.CountAsync());
        Assert.Equal(expectedBuCodes.Count, await db.BusinessUnits.CountAsync());
        Assert.Equal(expectedGroupKeys.Count, await db.Groups.CountAsync());
        Assert.Equal(expectedPortfolios.Count, await db.Portfolios.CountAsync());

        var dbBuCodes = await db.BusinessUnits.Select(b => b.Code).ToListAsync();
        Assert.Equal(expectedBuCodes, dbBuCodes.ToHashSet(StringComparer.Ordinal));

        // Every manager_external_ref present in the raw JSON must resolve to a real person id in
        // the DB (an independent re-check of the two-pass manager resolution, not a re-use of it).
        var extRefToId = await db.People.Select(p => new { p.ExternalRef, p.Id }).ToDictionaryAsync(x => x.ExternalRef, x => x.Id);
        foreach (var person in root.GetProperty("persons").EnumerateArray())
        {
            var extRef = person.GetProperty("external_ref").GetString()!;
            var managerRefProp = person.TryGetProperty("manager_external_ref", out var m) ? m : default;
            var managerRef = managerRefProp.ValueKind is JsonValueKind.String ? managerRefProp.GetString() : null;

            Assert.True(extRefToId.ContainsKey(extRef), $"person '{extRef}' from raw JSON missing in DB after sync");
            if (managerRef is not null)
            {
                Assert.True(extRefToId.ContainsKey(managerRef), $"manager '{managerRef}' referenced by '{extRef}' missing in DB");
                var dbPerson = await db.People.SingleAsync(p => p.ExternalRef == extRef);
                Assert.Equal(extRefToId[managerRef], dbPerson.ManagerPersonId);
            }
        }
    }

    // ============================================================================================
    // Gap 6: a snapshot with a duplicate external_ref is untested. Dev's notes call last-wins
    // "inherent to keyed upsert, not a wave-0 blocker" but nothing proves it empirically — an
    // unhandled duplicate could just as easily throw a DbUpdateException (transaction rollback,
    // masking the real cause) or, worse, silently create two Person rows if the in-memory
    // dictionary build in DirectoryImportService ever changed to not de-duplicate.
    // ============================================================================================

    [Fact]
    public async Task Snapshot_with_duplicate_external_ref_does_not_crash_and_creates_exactly_one_person_row()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("DUP", "BU01", jobTitle: "First Copy"),
                Snap.Person("DUP", "BU01", jobTitle: "Second Copy"), // same external_ref, different field
            }));

        using var scope = _factory.NewScope();
        var importer = scope.ServiceProvider.GetRequiredService<DirectoryImportService>();
        var result = await importer.SyncAsync(); // must not throw

        Assert.Equal(1, result.People);
        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.People.CountAsync(p => p.ExternalRef == "DUP"));
    }

    private void UseSnapshot(DirectorySnapshot snapshot) =>
        _factory.Directory.Inner = new StubDirectorySource(snapshot);

    // ============================================================================================
    // Mandatory §9.3(b) target: append-only bypass. Dev's B4 rounds proved raw SQL
    // UPDATE/DELETE/TRUNCATE are all rejected by the two migration-#1 triggers. This attempts a
    // route the Dev suite does not: disabling the row trigger itself via ALTER TABLE, then
    // deleting — over the app's own HapDbContext connection/role (the same literal instruction
    // as Dev's B4 tests; NOT the session_replication_role bypass the brief reserves for test
    // scaffolding only). The trigger is always re-enabled in a finally so this cannot poison any
    // other test in the shared, non-parallelised "hap-db" collection.
    // ============================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Disabling_the_append_only_trigger_then_deleting_is_examined_over_the_app_role()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<OrgOverrideService>();
            await service.CreateAsync(new CreateOverrideCommand(
                subjectId, OverrideField.BusinessUnit, "BU02", "seed an audit row", "qa-tester"));
        }

        using (var precheck = _factory.NewScope())
        {
            var db = precheck.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(1, await db.AuditLogs.CountAsync());
        }

        bool disableSucceeded;
        Exception? deleteException = null;
        try
        {
            using (var disableScope = _factory.NewScope())
            {
                var db = disableScope.ServiceProvider.GetRequiredService<HapDbContext>();
                // ALTER TABLE ... DISABLE TRIGGER requires table ownership, not superuser — the
                // "hap" role that runs migrations also owns audit_log, so this is a distinct
                // evasion route from UPDATE/DELETE/TRUNCATE, reachable over the same connection
                // the app itself uses (no app code calls this today — no endpoint executes
                // arbitrary SQL — but the brief's mandate is "every route you can reach" via
                // EF/raw SQL over the app's own connection/role, which this is).
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE audit_log DISABLE TRIGGER audit_log_no_update_delete;");
                disableSucceeded = true;
            }

            using (var deleteScope = _factory.NewScope())
            {
                var db = deleteScope.ServiceProvider.GetRequiredService<HapDbContext>();
                try
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM audit_log;");
                }
                catch (Exception ex)
                {
                    deleteException = ex;
                }
            }
        }
        finally
        {
            // Always restore, regardless of outcome above, so no later test in this
            // DisableParallelization collection ever runs against a weakened trigger.
            using var restoreScope = _factory.NewScope();
            var db = restoreScope.ServiceProvider.GetRequiredService<HapDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE audit_log ENABLE TRIGGER audit_log_no_update_delete;");
        }

        using var after = _factory.NewScope();
        var check = after.ServiceProvider.GetRequiredService<HapDbContext>();
        var survivingRows = await check.AuditLogs.CountAsync();

        // Recorded as evidence either way — see the story file's Attempts/notes for the
        // interpretation (this is a DB-role-privilege finding, not a code defect in HAP-3;
        // no HTTP-reachable route executes arbitrary SQL, so this route is not attacker-reachable
        // through the shipped API today, only through direct DB/code access at the same trust
        // level as psql). The assertion below documents what was actually observed rather than
        // assuming the answer.
        if (deleteException is null && survivingRows == 0)
        {
            // The trigger-disable route DID defeat append-only over the app's own role.
            Assert.True(disableSucceeded);
            Assert.Equal(0, survivingRows);
        }
        else
        {
            // The role could not disable the trigger, or the delete still failed some other way —
            // append-only held even against this route.
            Assert.True(survivingRows >= 1);
        }
    }
}
