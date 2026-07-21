using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Frameworks;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-6 (fresh-instance QA pass, CLAUDE.md §9). Two target
/// areas the Dev/panel rounds did not exercise end to end:
///
/// 1. FR-054's raw-factory gap (round-1/round-2 panel carry-forward): Dimension.Create and
///    LevelDescriptor.Create remain public static factories with no reference back to the owning
///    FrameworkVersion, so nothing stops a caller from constructing content for a locked version
///    and persisting it directly through EF, entirely bypassing FrameworkVersion.EnsureMutable().
///    Hap.Domain.Tests.FrameworkRawFactoryLockBypassTests proves this at the entity level with no
///    database involved; the tests here prove it end to end against a real migrated Postgres — a
///    write actually lands under a locked version's rows — and separately confirm the one HTTP
///    admin endpoint that writes framework content (POST /api/admin/frameworks) does NOT offer an
///    equivalent bypass, because re-seeding an existing version is a no-op by construction.
///
/// 2. Seeder idempotence under adversarial conditions the Dev suite's happy-path
///    "seed twice, second is a no-op" test did not cover: re-seeding after a manual row edit
///    (partial/corrupted state), and concurrent seed calls racing to create the same framework.
/// </summary>
[Collection("hap-db")]
public sealed class FrameworkLockBypassQaTests
{
    private readonly HapApiFactory _factory;

    public FrameworkLockBypassQaTests(HapApiFactory factory) => _factory = factory;

    // AUTH NOTE (post-HAP-4 rebase): /api/admin/frameworks now requires PlatformAdmin — bootstrap
    // one signed-in admin fixture per reset so every HTTP call in this file (including a fresh
    // client created after SeedAndLockV1Async) can authenticate via SignedInClientAsync.
    private async Task BootstrapAdminFixtureAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("FRAMEWORK-ADMIN", "BU01") }));
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("FRAMEWORK-ADMIN", role: "Platform Admin") });
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "FRAMEWORK-ADMIN");
        return client;
    }

    private async Task<FrameworkVersion> SeedAndLockV1Async()
    {
        await BootstrapAdminFixtureAsync();
        var client = await SignedInClientAsync();
        await client.PostAsync("/api/admin/frameworks", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var version = await db.FrameworkVersions.SingleAsync();
        version.Lock();
        await db.SaveChangesAsync();
        return version;
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_Dimension_Create_plus_direct_DbContext_Add_persists_content_under_a_locked_version()
    {
        var lockedVersion = await SeedAndLockV1Async();

        using (var writeScope = _factory.NewScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<HapDbContext>();
            // The exact bypass: never touch FrameworkVersion.AddDimension (the guarded "sole
            // path"), go straight to the public static factory + raw DbContext.Add/SaveChanges.
            var attackerDimension = Dimension.Create(lockedVersion.Id, "attacker-dimension", "Attacker Dimension", 99);
            db.Dimensions.Add(attackerDimension);
            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());

            Assert.Null(ex); // no domain guard, no FK/DB constraint stops this either
        }

        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(8, await verifyDb.Dimensions.CountAsync(d => d.FrameworkVersionId == lockedVersion.Id)); // 7 seeded + 1 injected
        Assert.True(await verifyDb.Dimensions.AnyAsync(d => d.Key == "attacker-dimension"));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Raw_LevelDescriptor_Create_plus_direct_DbContext_Add_persists_content_under_a_locked_version()
    {
        var lockedVersion = await SeedAndLockV1Async();

        Guid targetDimensionId;
        using (var readScope = _factory.NewScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<HapDbContext>();
            targetDimensionId = (await db.Dimensions.FirstAsync(d => d.FrameworkVersionId == lockedVersion.Id)).Id;
        }

        using (var writeScope = _factory.NewScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<HapDbContext>();
            var attackerDescriptor = LevelDescriptor.Create(targetDimensionId, 9, "Attacker Level", "attacker descriptor text");
            db.LevelDescriptors.Add(attackerDescriptor);
            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());

            Assert.Null(ex);
        }

        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.True(await verifyDb.LevelDescriptors.AnyAsync(l => l.Level == 9 && l.DimensionId == targetDimensionId));
    }

    [Fact]
    public async Task Guarded_AddDimension_path_correctly_rejects_the_same_write_the_raw_factory_allows()
    {
        // Contrast case: the same content, added through the domain-guarded "sole path", is
        // correctly rejected — proving this is specifically a raw-factory gap, not a general
        // failure of FR-054.
        var lockedVersion = await SeedAndLockV1Async();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var trackedVersion = await db.FrameworkVersions.SingleAsync(v => v.Id == lockedVersion.Id);

        Assert.Throws<FrameworkVersionLockedException>(() => trackedVersion.AddDimension("guarded-key", "Guarded Dimension", 100));
    }

    [Fact]
    public async Task Admin_reseed_endpoint_offers_no_equivalent_bypass_because_it_no_ops_for_an_existing_version()
    {
        // The only admin endpoint that writes framework content (POST /api/admin/frameworks) is
        // examined directly: does it offer an HTTP-reachable bypass? No — re-seeding an
        // already-seeded VersionNumber short-circuits before any AddDimension/AddLevelDescriptor
        // call (FrameworkSeeder's `if (versionIsNew)` guard), regardless of lock state. This is a
        // no-op-by-construction, not a guard defeating a write attempt, so it is not the same
        // finding as the raw-factory tests above — recorded here as the examined, closed path.
        var lockedVersion = await SeedAndLockV1Async();

        var client = await SignedInClientAsync();
        var response = await client.PostAsync("/api/admin/frameworks", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FrameworkSeedResult>();
        Assert.NotNull(result);
        Assert.False(result!.VersionCreated);
        Assert.Equal(0, result.DimensionsCreated);
        Assert.Equal(0, result.DescriptorsCreated);

        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(7, await verifyDb.Dimensions.CountAsync(d => d.FrameworkVersionId == lockedVersion.Id));
        Assert.Equal(28, await verifyDb.LevelDescriptors.CountAsync());
    }

    // ============================================================================================
    // §9.3(c)-style target adapted to this story's acceptance criteria: seeder idempotence under
    // adversarial re-runs (partial state, concurrent seed calls) — the Dev suite's own idempotency
    // test only exercises the clean happy path (seed, then seed again with nothing else touched).
    // ============================================================================================

    [Fact]
    public async Task Reseeding_after_a_manually_deleted_descriptor_row_does_not_restore_it_a_real_no_self_heal_finding()
    {
        await BootstrapAdminFixtureAsync();
        var client = await SignedInClientAsync();
        await client.PostAsync("/api/admin/frameworks", content: null);

        using (var corruptScope = _factory.NewScope())
        {
            var db = corruptScope.ServiceProvider.GetRequiredService<HapDbContext>();
            var oneDescriptor = await db.LevelDescriptors.FirstAsync();
            // Simulate partial/corrupted state (e.g. a bad manual fix, a botched prior migration
            // attempt) rather than a clean "never seeded" state. A leaf-table row (no FK
            // dependents), so the delete itself is uncontroversial — the interesting question is
            // whether re-seeding notices and heals it.
            db.LevelDescriptors.Remove(oneDescriptor);
            await db.SaveChangesAsync();
        }

        using (var precheck = _factory.NewScope())
        {
            var db = precheck.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(27, await db.LevelDescriptors.CountAsync());
            Assert.Equal(7, await db.Dimensions.CountAsync()); // dimensions themselves untouched
        }

        var reseedResponse = await client.PostAsync("/api/admin/frameworks", content: null);
        Assert.Equal(HttpStatusCode.OK, reseedResponse.StatusCode);
        var result = await reseedResponse.Content.ReadFromJsonAsync<FrameworkSeedResult>();
        Assert.False(result!.VersionCreated);
        Assert.Equal(0, result.DimensionsCreated);
        Assert.Equal(0, result.DescriptorsCreated);

        // Finding (not a HAP-6 acceptance-criterion breach — AC only requires "re-seed = no-op",
        // which this literally is): the seeder's content-creation loop is gated entirely behind
        // `versionIsNew`, so a corrupted/partially-deleted version's content is silently NOT
        // restored by re-seeding. Documented in the story file as a real, evidence-based gap for
        // whichever story adds admin tooling around framework content integrity.
        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(27, await verifyDb.LevelDescriptors.CountAsync()); // NOT restored to 28
    }

    [Fact]
    public async Task Concurrent_first_time_seed_calls_never_produce_duplicate_frameworks_or_partial_content()
    {
        await _factory.ResetAsync();

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = _factory.NewScope();
            var seeder = scope.ServiceProvider.GetRequiredService<FrameworkSeeder>();
            try
            {
                return (Result: (FrameworkSeedResult?)await seeder.SeedAsync(), Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Result: (FrameworkSeedResult?)null, Exception: ex);
            }
        }).ToArray();

        var outcomes = await Task.WhenAll(tasks);

        // At least one racer must succeed; any others may either succeed (idempotent re-read,
        // depending on commit ordering) or fail with a DB-level conflict (unique key on
        // frameworks.Key / (FrameworkId, VersionNumber)) — either outcome is acceptable, but the
        // end state must never show duplicate or partially-written rows.
        Assert.Contains(outcomes, o => o.Exception is null);

        using var verify = _factory.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.Frameworks.CountAsync());
        Assert.Equal(1, await db.FrameworkVersions.CountAsync());
        Assert.Equal(7, await db.Dimensions.CountAsync());
        Assert.Equal(28, await db.LevelDescriptors.CountAsync());
    }
}
