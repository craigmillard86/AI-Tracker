using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Cycles;
using Hap.Domain.Frameworks;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-7 (fresh-instance QA pass, CLAUDE.md §9), targeting the
/// two areas the story brief called out beyond the Dev/panel rounds' own coverage. Tests here are
/// QA work, attributed honestly — none of this existed during the Dev window.
///
/// 1. Late-override scope escapes the dev suite's single flat/cross-BU fixture did not exercise:
///    skip-level (grandchild), sibling-team, and upward (own manager) attempts, against a deeper
///    hand-built hierarchy. Also documents (examined and confirmed intentional, not "fixed") an
///    asymmetry: the Manager path checks target.IsActive (round-1 advisory fix), the PlatformAdmin
///    path does not — AC5's literal text, "Platform Admin (any person)", reads as deliberately
///    unrestricted, unlike the manager's "own directs only" scope.
///
/// 2. Framework-lock trigger (migration #3, round-1-fixed for the OLD/NEW-parent gap) bypass
///    attempts the existing round-1 regression test did not cover: re-parenting INTO a locked
///    version (not just out of one), in-place UPDATEs under lock with no re-parent, level_descriptor
///    re-parenting in both directions, DELETE of a locked framework_versions row and of dimension/
///    level_descriptor rows under lock (isolated from FK-Restrict noise with purpose-built empty
///    fixtures so the trigger itself — not an incidental FK — is what's proven), confirmation that
///    legitimate unlocked-version operations are unaffected (no false positives), and one genuine
///    bypass the round-1 fix did not and structurally cannot address: Postgres TRUNCATE does not
///    fire row-level triggers, and docker-compose's POSTGRES_USER=hap — the container's bootstrap
///    role, used identically for EF migrations and the running API (ConnectionStrings__Hap) — owns
///    every table it created, so the app's own connection can TRUNCATE any guarded table outright.
///    No code path in this build issues TRUNCATE outside TestSupport.ResetAsync (explicitly
///    test-only scaffolding), so this is not reachable through today's HTTP surface — recorded as a
///    residual finding, not a blocking defect for this L2 story, for whoever next adds raw SQL near
///    these tables.
/// </summary>
[Collection("hap-db")]
public sealed class CycleQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public CycleQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    // ============================================================================================
    // Target (a): late-override scope escapes against a deep hierarchy
    // ============================================================================================

    /// <summary>Hierarchy: ADMIN (PlatformAdmin) · MIDMGR (manages MGR1, MGR2; reports to nobody) ·
    /// MGR1 (reports to MIDMGR; manages EMP1) · MGR2 (reports to MIDMGR; manages EMP3 — MGR1's
    /// sibling team) · EMP1 (reports to MGR1; manages GRANDCHILD) · GRANDCHILD (reports to EMP1 —
    /// MGR1's skip-level-down grandchild) · EMP_OTHER_BU (BU02, unrelated) — one cycle opened and
    /// closed so late-override is reachable.</summary>
    private async Task<(HttpClient AdminClient, Guid CycleId, Dictionary<string, Guid> PersonIds)> DeepHierarchyClosedCycleAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MIDMGR", "BU01"),
                Snap.Person("MGR1", "BU01", managerExternalRef: "MIDMGR"),
                Snap.Person("MGR2", "BU01", managerExternalRef: "MIDMGR"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("GRANDCHILD", "BU01", managerExternalRef: "EMP1"),
                Snap.Person("EMP3", "BU01", managerExternalRef: "MGR2"),
                Snap.Person("EMP_OTHER_BU", "BU02"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var bu01 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU01");
            var bu02 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU02");
            bu01.SetOnboarded(true);
            bu02.SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MIDMGR", role: "Manager"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("MGR2", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Manager"),
            Snap.SeedUser("GRANDCHILD", role: "Individual"),
            Snap.SeedUser("EMP3", role: "Individual"),
            Snap.SeedUser("EMP_OTHER_BU", role: "Individual", buCode: "BU02"),
        });

        var adminClient = _factory.CreateClient();
        await HapApiFactory.SignInAsync(adminClient, "ADMIN");
        var seedResponse = await adminClient.PostAsync("/api/admin/frameworks", content: null);
        var seedResult = await seedResponse.Content.ReadFromJsonAsync<FrameworkSeedResult>();

        var createResponse = await adminClient.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(seedResult!.VersionId, "2026-08", true));
        var cycle = await createResponse.Content.ReadFromJsonAsync<CycleResponse>();
        await adminClient.PostAsync($"/api/cycles/{cycle!.Id}/open", content: null);
        await adminClient.PostAsync($"/api/cycles/{cycle.Id}/close", content: null);

        using var readScope = _factory.NewScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<HapDbContext>();
        var ids = await readDb.People.ToDictionaryAsync(p => p.ExternalRef, p => p.Id);

        return (adminClient, cycle.Id, ids);
    }

    private async Task<HttpClient> SignedInAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_for_a_skip_level_grandchild()
    {
        var (_, cycleId, ids) = await DeepHierarchyClosedCycleAsync();
        var mgr1Client = await SignedInAsync("MGR1");

        // GRANDCHILD reports to EMP1, who reports to MGR1 — not a DIRECT report of MGR1.
        var response = await mgr1Client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["GRANDCHILD"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False(await db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == ids["GRANDCHILD"]));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_for_a_sibling_teams_report()
    {
        var (_, cycleId, ids) = await DeepHierarchyClosedCycleAsync();
        var mgr1Client = await SignedInAsync("MGR1");

        // EMP3 reports to MGR2, MGR1's sibling under MIDMGR — not MGR1's report at all.
        var response = await mgr1Client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["EMP3"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False(await db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == ids["EMP3"]));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_for_their_own_manager_upward()
    {
        var (_, cycleId, ids) = await DeepHierarchyClosedCycleAsync();
        var mgr1Client = await SignedInAsync("MGR1");

        // MIDMGR is MGR1's OWN manager — upward, not a direct report.
        var response = await mgr1Client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["MIDMGR"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.False(await db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == ids["MIDMGR"]));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_cross_BU_for_an_unrelated_person()
    {
        var (_, cycleId, ids) = await DeepHierarchyClosedCycleAsync();
        var mgr1Client = await SignedInAsync("MGR1");

        var response = await mgr1Client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["EMP_OTHER_BU"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manager_CAN_grant_a_late_override_for_their_own_direct_report_in_the_deep_hierarchy_sanity_check()
    {
        // Negative control: proves the scope check in this fixture is discriminating (rejecting the
        // escapes above BECAUSE they're out of scope), not failing closed universally.
        var (_, cycleId, ids) = await DeepHierarchyClosedCycleAsync();
        var mgr1Client = await SignedInAsync("MGR1");

        var response = await mgr1Client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["EMP1"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdmin_late_override_does_not_check_target_activity_examined_and_confirmed_intentional()
    {
        // Documents an asymmetry deliberately, rather than silently: the Manager path checks
        // target.IsActive (round-1 advisory fix, mirroring ChainResolver); the PlatformAdmin path
        // does not. AC5's literal text — "Platform Admin (any person)" — reads as intentionally
        // unrestricted. Confirmed here as an examined design point: PlatformAdmin CAN grant a late
        // override for a person who has since departed.
        var (adminClient, cycleId, ids) = await DeepHierarchyClosedCycleAsync();

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var grandchild = await db.People.SingleAsync(p => p.Id == ids["GRANDCHILD"]);
            grandchild.Deactivate();
            await db.SaveChangesAsync();
        }

        var response = await adminClient.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(ids["GRANDCHILD"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task PlatformAdmin_late_override_for_a_nonexistent_person_is_a_clean_404_not_500()
    {
        // Re-confirmed independently against the deep-hierarchy fixture (dev's own equivalent test
        // uses the flat fixture) — same code path, different data, same expected outcome.
        var (adminClient, cycleId, _) = await DeepHierarchyClosedCycleAsync();

        var response = await adminClient.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ============================================================================================
    // Target (b): framework-lock trigger (migration #3) — bypasses beyond the round-1 regression
    // ============================================================================================

    private async Task<HttpClient> FrameworkAdminClientAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("FW-ADMIN", "BU01") }));
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("FW-ADMIN", role: "Platform Admin") });

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "FW-ADMIN");
        await client.PostAsync("/api/admin/frameworks", content: null);
        return client;
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_UPDATE_reparenting_a_dimension_INTO_a_locked_version_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var unlockedDraft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-reparent-into-locked");
        var dimensionUnderUnlocked = Dimension.Create(unlockedDraft.Id, "qa-unlocked-dim", "QA Unlocked Dim", 999);
        db.Dimensions.Add(dimensionUnderUnlocked);
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dimensions SET \"FrameworkVersionId\" = {lockedVersion.Id} WHERE \"Id\" = {dimensionUnderUnlocked.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());

        var stillUnlocked = await db.Dimensions.AsNoTracking().SingleAsync(d => d.Id == dimensionUnderUnlocked.Id);
        Assert.Equal(unlockedDraft.Id, stillUnlocked.FrameworkVersionId); // re-parent into locked did NOT stick
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_UPDATE_in_place_on_a_dimension_under_a_locked_version_with_no_reparent_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        var targetDimension = await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id);
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var originalName = targetDimension.Name;
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dimensions SET \"Name\" = 'qa-hacked-name' WHERE \"Id\" = {targetDimension.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());

        var unchanged = await db.Dimensions.AsNoTracking().SingleAsync(d => d.Id == targetDimension.Id);
        Assert.Equal(originalName, unchanged.Name);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_UPDATE_in_place_on_a_level_descriptor_under_a_locked_version_with_no_reparent_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        var dimensionId = (await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id)).Id;
        var targetDescriptor = await db.LevelDescriptors.FirstAsync(l => l.DimensionId == dimensionId);
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var originalText = targetDescriptor.DescriptorText;
        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE level_descriptors SET \"DescriptorText\" = 'qa-hacked-text' WHERE \"Id\" = {targetDescriptor.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());

        var unchanged = await db.LevelDescriptors.AsNoTracking().SingleAsync(l => l.Id == targetDescriptor.Id);
        Assert.Equal(originalText, unchanged.DescriptorText);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_UPDATE_reparenting_a_level_descriptor_OUT_of_a_locked_dimension_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        var lockedDimensionId = (await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id)).Id;
        var descriptorUnderLocked = await db.LevelDescriptors.FirstAsync(l => l.DimensionId == lockedDimensionId);
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var unlockedDraft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-descriptor-reparent-out");
        var unlockedDimension = Dimension.Create(unlockedDraft.Id, "qa-unlocked-dim-2", "QA Unlocked Dim 2", 998);
        db.Dimensions.Add(unlockedDimension);
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE level_descriptors SET \"DimensionId\" = {unlockedDimension.Id} WHERE \"Id\" = {descriptorUnderLocked.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());

        var stillOwned = await db.LevelDescriptors.AsNoTracking().SingleAsync(l => l.Id == descriptorUnderLocked.Id);
        Assert.Equal(lockedDimensionId, stillOwned.DimensionId);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_UPDATE_reparenting_a_level_descriptor_INTO_a_locked_dimension_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        var lockedDimensionId = (await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id)).Id;

        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var unlockedDraft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-descriptor-reparent-in");
        var unlockedDimension = Dimension.Create(unlockedDraft.Id, "qa-unlocked-dim-3", "QA Unlocked Dim 3", 997);
        db.Dimensions.Add(unlockedDimension);
        await db.SaveChangesAsync();
        var descriptorUnderUnlocked = LevelDescriptor.Create(unlockedDimension.Id, 0, "QA Level 0", "qa descriptor text");
        db.LevelDescriptors.Add(descriptorUnderUnlocked);
        await db.SaveChangesAsync();

        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE level_descriptors SET \"DimensionId\" = {lockedDimensionId} WHERE \"Id\" = {descriptorUnderUnlocked.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());

        var stillUnlocked = await db.LevelDescriptors.AsNoTracking().SingleAsync(l => l.Id == descriptorUnderUnlocked.Id);
        Assert.Equal(unlockedDimension.Id, stillUnlocked.DimensionId); // re-parent into locked did NOT stick
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_DELETE_of_a_dimension_under_a_locked_version_is_rejected_isolated_from_FK_by_a_descriptor_free_fixture()
    {
        // Isolated from FK-Restrict noise deliberately: a dimension with real level_descriptors
        // would be blocked by FK Restrict regardless of the lock trigger, which would prove nothing
        // about the trigger specifically. This dimension has ZERO descriptors, so only the trigger
        // can be stopping the delete.
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var draft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-isolated-dimension-delete");
        var isolatedDimension = Dimension.Create(draft.Id, "qa-isolated-dim", "QA Isolated Dim", 996);
        db.Dimensions.Add(isolatedDimension);
        await db.SaveChangesAsync();

        var trackedDraft = await db.FrameworkVersions.SingleAsync(v => v.Id == draft.Id);
        trackedDraft.Lock();
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dimensions WHERE \"Id\" = {isolatedDimension.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());
        Assert.True(await db.Dimensions.AnyAsync(d => d.Id == isolatedDimension.Id));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_DELETE_of_a_level_descriptor_under_a_locked_version_is_rejected()
    {
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        var dimensionId = (await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id)).Id;
        var targetDescriptor = await db.LevelDescriptors.FirstAsync(l => l.DimensionId == dimensionId);
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM level_descriptors WHERE \"Id\" = {targetDescriptor.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());
        Assert.True(await db.LevelDescriptors.AnyAsync(l => l.Id == targetDescriptor.Id));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_DELETE_of_a_locked_framework_versions_row_is_rejected_isolated_from_FK_by_a_dimension_free_fixture()
    {
        // Isolated the same way as the dimension-delete test above: a version WITH dimensions would
        // be blocked by FK Restrict from dimensions regardless of the trigger. This version has ZERO
        // dimensions, so only the framework_versions_locked_guard trigger can be stopping the delete.
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var emptyDraft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-isolated-version-delete");

        var trackedDraft = await db.FrameworkVersions.SingleAsync(v => v.Id == emptyDraft.Id);
        trackedDraft.Lock();
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM framework_versions WHERE \"Id\" = {emptyDraft.Id}"));

        Assert.NotNull(ex);
        Assert.Contains("FR-054", ex!.ToString());
        Assert.True(await db.FrameworkVersions.AnyAsync(v => v.Id == emptyDraft.Id));
    }

    [Fact]
    public async Task Legitimate_unlocked_version_raw_SQL_operations_are_unaffected_no_false_positives()
    {
        // Independently re-derives the L2 panel round-2 "no false positive" confirmation rather than
        // trusting Dev's own claim — insert, in-place update, re-parent between two unlocked
        // versions, and delete, all against still-unlocked content, all via raw SQL.
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
        var draftA = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-legit-draft-a");
        var draftB = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "qa-legit-draft-b");

        // INSERT under unlocked.
        var newDimensionId = Guid.NewGuid();
        var insertEx = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dimensions (""Id"", ""FrameworkVersionId"", ""Key"", ""Name"", ""DisplayOrder"", ""CreatedAt"")
            VALUES ({newDimensionId}, {draftA.Id}, 'qa-legit-dim', 'QA Legit Dim', 1, now())"));
        Assert.Null(insertEx);

        // UPDATE (in-place) under unlocked.
        var updateEx = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dimensions SET \"Name\" = 'QA Legit Dim Renamed' WHERE \"Id\" = {newDimensionId}"));
        Assert.Null(updateEx);
        var renamed = await db.Dimensions.AsNoTracking().SingleAsync(d => d.Id == newDimensionId);
        Assert.Equal("QA Legit Dim Renamed", renamed.Name);

        // Re-parent between two UNLOCKED versions.
        var reparentEx = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dimensions SET \"FrameworkVersionId\" = {draftB.Id} WHERE \"Id\" = {newDimensionId}"));
        Assert.Null(reparentEx);
        var reparented = await db.Dimensions.AsNoTracking().SingleAsync(d => d.Id == newDimensionId);
        Assert.Equal(draftB.Id, reparented.FrameworkVersionId);

        // DELETE under unlocked.
        var deleteEx = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dimensions WHERE \"Id\" = {newDimensionId}"));
        Assert.Null(deleteEx);
        Assert.False(await db.Dimensions.AnyAsync(d => d.Id == newDimensionId));

        // DELETE an unlocked, childless framework_version.
        var versionDeleteEx = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM framework_versions WHERE \"Id\" = {draftB.Id}"));
        Assert.Null(versionDeleteEx);
        Assert.False(await db.FrameworkVersions.AnyAsync(v => v.Id == draftB.Id));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task TRUNCATE_bypasses_the_row_level_lock_trigger_a_genuine_residual_finding()
    {
        // GENUINE FINDING (not a blocking defect for this L2 story — see class doc): Postgres
        // row-level BEFORE triggers (what migration #3 installs) do NOT fire on TRUNCATE — only
        // statement-level triggers do, and none is defined here. Separately, docker-compose's
        // POSTGRES_USER=hap is postgres:16-alpine's bootstrap role — the actual owner of every table
        // it creates via migrations — and it is the SAME role the running API connects as
        // (ConnectionStrings__Hap), so this is not a test-only superuser artifact: the app's own
        // production-shaped local connection genuinely has TRUNCATE rights on these tables. This
        // test proves the gap directly rather than asserting rejection: TRUNCATE on the guarded leaf
        // table succeeds and empties it — including the locked version's seeded rows — even though
        // every DELETE/UPDATE/INSERT test in this file (and the round-1 regression test) is
        // correctly rejected. No code path in this build issues TRUNCATE outside
        // TestSupport.ResetAsync (explicit test-only scaffolding, itself using a DIFFERENT mechanism
        // — session_replication_role — for the same underlying reason: the guard is a per-statement
        // trigger, not a privilege restriction), so nothing here is reachable through today's HTTP
        // surface. Recorded for whoever next adds raw SQL near these tables (an admin reset tool, a
        // bulk-import job) — the trigger is a backstop against row-level writes, not a substitute
        // for withholding TRUNCATE/DDL privilege from the application's own DB role.
        var client = await FrameworkAdminClientAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var lockedVersion = await db.FrameworkVersions.SingleAsync();
        lockedVersion.Lock();
        await db.SaveChangesAsync();

        var beforeCount = await db.LevelDescriptors.CountAsync();
        Assert.Equal(28, beforeCount); // sanity: the locked version's seeded content is present

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE level_descriptors"));

        var afterCount = await db.LevelDescriptors.CountAsync();

        // The finding IS this assertion pair: TRUNCATE succeeds (no exception) and empties the
        // table (0 rows) despite every row belonging to a locked version — the opposite outcome of
        // every DELETE/UPDATE/INSERT bypass attempt above.
        Assert.Null(ex);
        Assert.Equal(0, afterCount);
    }

    // ============================================================================================
    // Target (d): invitation-generation integrity
    // ============================================================================================

    /// <summary>ADMIN2 (Platform Admin) · ACTIVE1 (active, invited) · LEAVER1 (inactive from before
    /// the cycle ever opens) — one onboarded BU, cycle created and opened (not closed, so the
    /// invitation snapshot is the thing under test).</summary>
    private async Task<(HttpClient AdminClient, Guid CycleId, Dictionary<string, Guid> PersonIds)> OpenCycleWithInactivePersonFixtureAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("ADMIN2", "BU01"),
                Snap.Person("ACTIVE1", "BU01"),
                Snap.Person("LEAVER1", "BU01", isActive: false),
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
            Snap.SeedUser("ADMIN2", role: "Platform Admin"),
            Snap.SeedUser("ACTIVE1", role: "Individual"),
        });

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "ADMIN2");
        var seedResponse = await client.PostAsync("/api/admin/frameworks", content: null);
        var seedResult = await seedResponse.Content.ReadFromJsonAsync<FrameworkSeedResult>();

        var createResponse = await client.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(seedResult!.VersionId, "2026-08", true));
        var cycle = await createResponse.Content.ReadFromJsonAsync<CycleResponse>();
        await client.PostAsync($"/api/cycles/{cycle!.Id}/open", content: null);

        using var readScope = _factory.NewScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<HapDbContext>();
        // .IgnoreQueryFilters()-free direct query: LEAVER1 is inactive but still a People row.
        var ids = await readDb.People.ToDictionaryAsync(p => p.ExternalRef, p => p.Id);

        return (client, cycle.Id, ids);
    }

    [Fact]
    public async Task Inactive_person_at_cycle_open_gets_no_invitation_row_at_all_not_even_excluded()
    {
        // CycleService.OpenAsync filters to IsActive people before generating the snapshot, so an
        // inactive person gets NEITHER an Invited nor an ExcludedFor row — confirmed directly rather
        // than assumed, since InvitationExclusionReason has no "Inactive" case to record it under.
        var (_, cycleId, ids) = await OpenCycleWithInactivePersonFixtureAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var leaverRowCount = await db.CycleInvitations.CountAsync(i => i.CycleId == cycleId && i.PersonId == ids["LEAVER1"]);

        Assert.Equal(0, leaverRowCount);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_SQL_duplicate_invitation_row_for_the_same_cycle_and_person_is_rejected_by_the_unique_index()
    {
        var (_, cycleId, ids) = await OpenCycleWithInactivePersonFixtureAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var activeId = ids["ACTIVE1"];

        var ex = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO cycle_invitations (""Id"", ""CycleId"", ""PersonId"", ""InvitedAt"", ""Excluded"", ""ExcludedReason"", ""CreatedAt"")
            VALUES ({Guid.NewGuid()}, {cycleId}, {activeId}, now(), false, NULL, now())"));

        Assert.NotNull(ex);

        var rowCount = await db.CycleInvitations.CountAsync(i => i.CycleId == cycleId && i.PersonId == activeId);
        Assert.Equal(1, rowCount); // still exactly the one row from open-time generation
    }
}
