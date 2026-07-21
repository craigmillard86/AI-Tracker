using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

[Collection("hap-db")]
public sealed class OrgOverrideAuditTests
{
    private readonly HapApiFactory _factory;

    public OrgOverrideAuditTests(HapApiFactory factory) => _factory = factory;

    private async Task SeedTwoBuSnapshotAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("LEAD", "BU01"),
                Snap.Person("SUBJECT", "BU01", managerExternalRef: "LEAD"),
            }));
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
    }

    private async Task<Guid> SubjectIdAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == "SUBJECT")).Id;
    }

    // --- Criterion 4: exactly one audit row per override write (PrivacyReporting) ------------
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Override_write_produces_exactly_one_audit_row()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = subjectId,
            field = "BusinessUnit",
            overrideValue = "BU02",
            reason = "correcting a mis-mapped BU",
            createdBy = "tester",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.OrgOverrides.CountAsync());
        var audits = await db.AuditLogs.ToListAsync();
        Assert.Single(audits);
        Assert.Equal(AuditAction.OrgOverride, audits[0].Action);
        Assert.Equal(subjectId, audits[0].SubjectPersonId);
    }

    // --- Criterion 6: audit failure fails the operation closed (PrivacyReporting) ------------
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Audit_write_failure_rolls_back_the_override_write()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            // A service whose audit writer always fails: the override + audit share one unit of
            // work, so the failure must leave nothing behind.
            var service = new OrgOverrideService(db, new ThrowingAuditWriter());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new CreateOverrideCommand(
                    subjectId, OverrideField.BusinessUnit, "BU02", "should not persist", "tester")));
        }

        using var verify = _factory.NewScope();
        var check = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await check.OrgOverrides.CountAsync());
        Assert.Equal(0, await check.AuditLogs.CountAsync());

        // And the subject's BU was not changed by the failed attempt.
        var subject = await check.People.SingleAsync(p => p.Id == subjectId);
        var bu01 = await check.BusinessUnits.SingleAsync(b => b.Code == "BU01");
        Assert.Equal(bu01.Id, subject.BusinessUnitId);
    }

    // --- Criterion 3: overrides survive re-sync and re-apply --------------------------------
    [Fact]
    public async Task Override_survives_resync_and_reapplies_over_directory_data()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        // Correct SUBJECT into BU02 (the directory keeps saying BU01).
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<OrgOverrideService>();
            await service.CreateAsync(new CreateOverrideCommand(
                subjectId, OverrideField.BusinessUnit, "BU02", "correcting a mis-mapped BU", "tester"));
        }

        // Re-sync the same directory snapshot (SUBJECT still mapped to BU01 there).
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }

        using var verify = _factory.NewScope();
        var check = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        var subject = await check.People.SingleAsync(p => p.Id == subjectId);
        var bu02 = await check.BusinessUnits.SingleAsync(b => b.Code == "BU02");

        Assert.Equal(bu02.Id, subject.BusinessUnitId);              // override re-applied after import
        Assert.Equal(1, await check.OrgOverrides.CountAsync());     // override row survived re-sync
    }

    // --- Criterion 8 (build-side guard): no endpoint creates or mutates a person ------------
    [Theory]
    [InlineData("POST", "/api/persons")]
    [InlineData("POST", "/api/people")]
    [InlineData("PUT", "/api/people/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/people")]
    public async Task No_person_mutation_endpoint_exists(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { externalRef = "HACK", displayName = "Intruder" }),
        };
        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} {path} should not exist (got {(int)response.StatusCode}); people mutate only via sync.");
    }

    // --- B3: an unsatisfiable override is rejected before anything is written (fail closed) ---
    private OrgOverrideService NewOverrideService(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<OrgOverrideService>();

    [Fact]
    public async Task Override_with_unresolvable_business_unit_is_rejected_and_writes_nothing()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            await Assert.ThrowsAsync<OverrideValidationException>(() =>
                NewOverrideService(scope).CreateAsync(new CreateOverrideCommand(
                    subjectId, OverrideField.BusinessUnit, "BU99", "no such BU", "tester")));
        }

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db.OrgOverrides.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Override_setting_a_person_as_their_own_manager_is_rejected()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            await Assert.ThrowsAsync<OverrideValidationException>(() =>
                NewOverrideService(scope).CreateAsync(new CreateOverrideCommand(
                    subjectId, OverrideField.Manager, "SUBJECT", "self", "tester")));
        }

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db.OrgOverrides.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Override_creating_a_management_chain_cycle_is_rejected()
    {
        await SeedTwoBuSnapshotAsync(); // SUBJECT reports to LEAD
        Guid leadId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            leadId = (await db.People.SingleAsync(p => p.ExternalRef == "LEAD")).Id;
        }

        // Setting LEAD's manager to SUBJECT closes the loop SUBJECT -> LEAD -> SUBJECT.
        using (var scope = _factory.NewScope())
        {
            await Assert.ThrowsAsync<OverrideValidationException>(() =>
                NewOverrideService(scope).CreateAsync(new CreateOverrideCommand(
                    leadId, OverrideField.Manager, "SUBJECT", "cycle", "tester")));
        }

        using var verify = _factory.NewScope();
        var db2 = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db2.OrgOverrides.CountAsync());
        Assert.Equal(0, await db2.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Override_endpoint_returns_422_for_unresolvable_value()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = subjectId,
            field = "Manager",
            overrideValue = "NO-SUCH-PERSON",
            reason = "bad ref",
            createdBy = "tester",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- B4: the database itself rejects UPDATE/DELETE on audit_log (PrivacyReporting) --------
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Database_rejects_raw_update_and_delete_on_audit_log()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        // Produce one genuine audit row through a valid override.
        using (var scope = _factory.NewScope())
        {
            await NewOverrideService(scope).CreateAsync(new CreateOverrideCommand(
                subjectId, OverrideField.BusinessUnit, "BU02", "seed an audit row", "tester"));
        }

        using (var precheck = _factory.NewScope())
        {
            var db = precheck.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(1, await db.AuditLogs.CountAsync());
        }

        // Each raw statement in its own scope so a Postgres error cannot carry connection state.
        using (var updateScope = _factory.NewScope())
        {
            var db = updateScope.ServiceProvider.GetRequiredService<HapDbContext>();
            // Quoted PascalCase column (EF keeps it case-sensitive) so the statement reaches
            // execution and fires the trigger; no braces (ExecuteSqlRaw treats {n} as parameters).
            var update = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE audit_log SET \"Detail\" = \"Detail\";"));
            Assert.Contains("append-only", update.Message, StringComparison.OrdinalIgnoreCase);
        }

        using (var deleteScope = _factory.NewScope())
        {
            var db = deleteScope.ServiceProvider.GetRequiredService<HapDbContext>();
            var delete = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM audit_log;"));
            Assert.Contains("append-only", delete.Message, StringComparison.OrdinalIgnoreCase);
        }

        // The row is untouched.
        using var after = _factory.NewScope();
        var db2 = after.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db2.AuditLogs.CountAsync());
    }

    // --- B4 (TRUNCATE route): the database also rejects TRUNCATE of audit_log (PrivacyReporting) --
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Database_rejects_raw_truncate_of_audit_log()
    {
        await SeedTwoBuSnapshotAsync();
        var subjectId = await SubjectIdAsync();

        using (var scope = _factory.NewScope())
        {
            await NewOverrideService(scope).CreateAsync(new CreateOverrideCommand(
                subjectId, OverrideField.BusinessUnit, "BU02", "seed an audit row", "tester"));
        }

        // Raw TRUNCATE over the app's own connection/role — a row trigger would not fire on TRUNCATE,
        // so this proves the dedicated statement-level BEFORE TRUNCATE trigger.
        using (var truncateScope = _factory.NewScope())
        {
            var db = truncateScope.ServiceProvider.GetRequiredService<HapDbContext>();
            var truncate = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("TRUNCATE audit_log;"));
            Assert.Contains("append-only", truncate.Message, StringComparison.OrdinalIgnoreCase);
        }

        using var after = _factory.NewScope();
        var db2 = after.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db2.AuditLogs.CountAsync());
    }
}
