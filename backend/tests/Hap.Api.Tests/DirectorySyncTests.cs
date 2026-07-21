using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using DirectorySnapshot = Hap.Infrastructure.Directory.DirectorySnapshot;

namespace Hap.Api.Tests;

[Collection("hap-db")]
public sealed class DirectorySyncTests
{
    private readonly HapApiFactory _factory;

    public DirectorySyncTests(HapApiFactory factory) => _factory = factory;

    private async Task<DirectoryImportResult> SyncAsync()
    {
        using var scope = _factory.NewScope();
        var importer = scope.ServiceProvider.GetRequiredService<DirectoryImportService>();
        return await importer.SyncAsync();
    }

    private void UseSnapshot(DirectorySnapshot snapshot) =>
        _factory.Directory.Inner = new StubDirectorySource(snapshot);

    // --- Criterion 1: full canonical snapshot imports with matching counts + spot rows ------
    [Fact]
    public async Task Sync_imports_full_canonical_snapshot_with_matching_counts_and_spot_rows()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/admin/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expected = DirectoryGenerator.Generate(Distributions.CanonicalSeed).Snapshot;

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();

        Assert.Equal(expected.Persons.Count, await db.People.CountAsync());
        Assert.Equal(23, await db.BusinessUnits.CountAsync());
        Assert.Equal(6, await db.Groups.CountAsync());
        Assert.Equal(3, await db.Portfolios.CountAsync());

        // Spot row: the HIG Executive is the org root — present, active, null manager.
        var exec = await db.People.SingleAsync(p => p.ExternalRef == Distributions.ExecRef);
        Assert.Null(exec.ManagerPersonId);
        Assert.True(exec.IsActive);

        // Spot manager link: the seeded individual reports to the seeded manager (resolved to an id).
        var manager = await db.People.SingleAsync(p => p.ExternalRef == Distributions.SeedManagerRef);
        var individual = await db.People.SingleAsync(p => p.ExternalRef == Distributions.SeedIndividualRef);
        Assert.Equal(manager.Id, individual.ManagerPersonId);

        // Spot leaver: the engineered leaver is imported inactive, retained (FR-024).
        var leaver = await db.People.SingleAsync(p => p.ExternalRef == Distributions.LeaverRef);
        Assert.False(leaver.IsActive);

        // Spot contractor classification round-trips.
        var contractorManager = await db.People.SingleAsync(p => p.ExternalRef == Distributions.ContractorManagerRef);
        Assert.Equal(EmployeeType.Contractor, contractorManager.EmployeeType);
    }

    // --- Criterion 2: idempotent, no duplicates ---------------------------------------------
    [Fact]
    public async Task Sync_is_idempotent_running_twice_produces_no_duplicates()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("P-LEAD", "BU01"),
                Snap.Person("P-1", "BU01", managerExternalRef: "P-LEAD"),
                Snap.Person("P-2", "BU01", managerExternalRef: "P-LEAD"),
            }));

        var first = await SyncAsync();
        var second = await SyncAsync();

        Assert.Equal(3, first.People);
        Assert.Equal(second.People, first.People);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(3, await db.People.CountAsync());
        Assert.Equal(1, await db.BusinessUnits.CountAsync());
    }

    // --- Criterion 2: leaver becomes inactive, never deleted --------------------------------
    [Fact]
    public async Task Person_dropped_from_snapshot_is_deactivated_and_retained()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("P-LEAD", "BU01"),
                Snap.Person("P-STAYER", "BU01", managerExternalRef: "P-LEAD"),
                Snap.Person("P-LEAVER", "BU01", managerExternalRef: "P-LEAD"),
            }));
        await SyncAsync();

        // Next snapshot no longer contains P-LEAVER.
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("P-LEAD", "BU01"),
                Snap.Person("P-STAYER", "BU01", managerExternalRef: "P-LEAD"),
            }));
        var result = await SyncAsync();

        Assert.Equal(1, result.Deactivated);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var leaver = await db.People.SingleAsync(p => p.ExternalRef == "P-LEAVER"); // still present
        Assert.False(leaver.IsActive);
        Assert.Equal(3, await db.People.CountAsync()); // nothing deleted
    }

    [Fact]
    public async Task Person_flagged_inactive_in_snapshot_is_stored_inactive()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("P-LEAD", "BU01"),
                Snap.Person("P-GONE", "BU01", managerExternalRef: "P-LEAD", isActive: false),
            }));
        await SyncAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False((await db.People.SingleAsync(p => p.ExternalRef == "P-GONE")).IsActive);
    }

    // --- Criterion 3: manager / BU change applied on next sync ------------------------------
    [Fact]
    public async Task Manager_and_business_unit_changes_are_applied_on_next_sync()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("MGR-A", "BU01"),
                Snap.Person("MGR-B", "BU02"),
                Snap.Person("MOVER", "BU01", managerExternalRef: "MGR-A"),
            }));
        await SyncAsync();

        // MOVER changes manager (A->B) and BU (BU01->BU02).
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("MGR-A", "BU01"),
                Snap.Person("MGR-B", "BU02"),
                Snap.Person("MOVER", "BU02", managerExternalRef: "MGR-B"),
            }));
        await SyncAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var mover = await db.People.SingleAsync(p => p.ExternalRef == "MOVER");
        var mgrB = await db.People.SingleAsync(p => p.ExternalRef == "MGR-B");
        var bu02 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU02");
        Assert.Equal(mgrB.Id, mover.ManagerPersonId);
        Assert.Equal(bu02.Id, mover.BusinessUnitId);
    }

    // --- B2: a corrupt manager reference fails the whole import, not silently null -----------
    [Fact]
    public async Task Sync_throws_when_a_manager_reference_does_not_resolve()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("LEAD", "BU01"),
                Snap.Person("ORPHAN", "BU01", managerExternalRef: "GHOST"), // GHOST not in snapshot
            }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(SyncAsync);
        Assert.Contains("unknown manager", ex.Message);

        // Nothing committed — the failed import left no people.
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await db.People.CountAsync());
    }

    [Fact]
    public async Task Sync_throws_when_a_person_is_their_own_manager()
    {
        await _factory.ResetAsync();
        UseSnapshot(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("SELF", "BU01", managerExternalRef: "SELF") }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(SyncAsync);
        Assert.Contains("its own manager", ex.Message);
    }
}
