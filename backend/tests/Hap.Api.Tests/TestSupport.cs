using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Audit;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

// Both Hap.Synth and the directory port define a "DirectorySnapshot"; in tests the port type
// (what the importer consumes) is the one we mean.
using DirectorySnapshot = Hap.Infrastructure.Directory.DirectorySnapshot;

namespace Hap.Api.Tests;

/// <summary>
/// The web-app factory for HAP-3 integration tests. It writes the canonical synthetic
/// directory to a temp file and points a swappable <see cref="IDirectorySource"/> at it, so the
/// default sync path runs against genuine generator output; individual tests can swap in a
/// small hand-built snapshot to exercise a precise behaviour. Runs against the disposable
/// Postgres that verify.sh provisions via <c>HAP_DB_CONNECTION</c>.
/// </summary>
public sealed class HapApiFactory : WebApplicationFactory<Program>
{
    public string CanonicalSnapshotPath { get; }
    public SwappableDirectorySource Directory { get; } = new();
    public SwappableSeedUserSource SeedUsers { get; } = new();

    /// <summary>The canonical generator's seed-user list (HAP-2), for tests that want the real
    /// seven-role fixture without hand-building one.</summary>
    public IReadOnlyList<SeedUserRecord> CanonicalSeedUsers { get; }

    public HapApiFactory()
    {
        CanonicalSnapshotPath = Path.Combine(Path.GetTempPath(), $"hap3-dir-{Guid.NewGuid():N}.json");
        var generated = DirectoryGenerator.Generate(Distributions.CanonicalSeed);
        File.WriteAllText(CanonicalSnapshotPath, SnapshotSerializer.SerializeSnapshot(generated.Snapshot));
        Directory.Inner = new SyntheticDirectoryAdapter(CanonicalSnapshotPath);

        CanonicalSeedUsers = generated.SeedUsers.Users
            .Select(u => new SeedUserRecord
            {
                Role = u.Role,
                ExternalRef = u.ExternalRef,
                Name = u.Name,
                Email = u.Email,
                BuCode = u.BuCode,
            })
            .ToList();
        SeedUsers.Inner = new StubSeedUserSource(CanonicalSeedUsers);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The gate of record points migrations + tests at a disposable, random-port Postgres via
        // HAP_DB_CONNECTION. appsettings.json pins ConnectionStrings:Hap to a fixed localhost port,
        // and Program.cs captures that value at build time — so the hosted test app must have its
        // DbContext registration replaced (ConfigureTestServices runs last) to use the test database.
        var testConnection = Environment.GetEnvironmentVariable("HAP_DB_CONNECTION");

        builder.ConfigureTestServices(services =>
        {
            if (!string.IsNullOrWhiteSpace(testConnection))
            {
                services.RemoveAll<DbContextOptions<HapDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<HapDbContext>(o => o.UseNpgsql(testConnection));
            }

            services.RemoveAll<IDirectorySource>();
            services.AddSingleton<IDirectorySource>(Directory);

            services.RemoveAll<ISeedUserSource>();
            services.AddSingleton<ISeedUserSource>(SeedUsers);
        });
    }

    /// <summary>Signs in as <paramref name="externalRef"/> on <paramref name="client"/> (the client
    /// must have been created with cookie handling, which <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>
    /// enables by default) and returns the raw response so callers can assert on status/body too.</summary>
    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, string externalRef) =>
        client.PostAsJsonAsync("/auth/signin", new SignInRequest(externalRef));

    /// <summary>Truncate every HAP table so each test starts from an empty schema.
    /// The audit_log append-only trigger blocks TRUNCATE (by design, FR-053); this reset is the
    /// one legitimate exception, so it briefly drops into <c>session_replication_role='replica'</c>
    /// to bypass the trigger for the wipe, then restores it. This works only because the disposable
    /// TEST database role is a superuser — the application never runs with that role and never calls
    /// this path; it is test scaffolding only.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SET session_replication_role = 'replica'; " +
            "TRUNCATE audit_log, org_overrides, role_grants, people, business_units, groups, portfolios, " +
            "level_descriptors, dimensions, framework_versions, frameworks, " +
            "cycle_invitations, cycle_late_overrides, cycles, " +
            "initiatives, harris_categories, harris_stage_map RESTART IDENTITY CASCADE; " +
            "SET session_replication_role = 'origin';");
    }

    public IServiceScope NewScope() => Services.CreateScope();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(CanonicalSnapshotPath))
        {
            File.Delete(CanonicalSnapshotPath);
        }
    }
}

[CollectionDefinition("hap-db", DisableParallelization = true)]
public sealed class HapDbCollection : ICollectionFixture<HapApiFactory>
{
}

/// <summary>An <see cref="IDirectorySource"/> whose backing source can be swapped between tests.</summary>
public sealed class SwappableDirectorySource : IDirectorySource
{
    public IDirectorySource Inner { get; set; } = default!;

    public Task<DirectorySnapshot> FetchSnapshotAsync(CancellationToken cancellationToken = default) =>
        Inner.FetchSnapshotAsync(cancellationToken);
}

/// <summary>Returns a fixed, in-memory snapshot; mutable so a test can re-sync a changed one.</summary>
public sealed class StubDirectorySource : IDirectorySource
{
    private DirectorySnapshot _snapshot;

    public StubDirectorySource(DirectorySnapshot snapshot) => _snapshot = snapshot;

    public void Set(DirectorySnapshot snapshot) => _snapshot = snapshot;

    public Task<DirectorySnapshot> FetchSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);
}

/// <summary>An audit writer that always fails — used to prove the fail-closed guarantee.</summary>
public sealed class ThrowingAuditWriter : IAuditWriter
{
    public void Record(AuditLog entry) =>
        throw new InvalidOperationException("audit subsystem unavailable (injected test fault)");
}

/// <summary>An <see cref="ISeedUserSource"/> whose backing source can be swapped between tests
/// (mirrors <see cref="SwappableDirectorySource"/>).</summary>
public sealed class SwappableSeedUserSource : ISeedUserSource
{
    public ISeedUserSource Inner { get; set; } = default!;

    public Task<IReadOnlyList<SeedUserRecord>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        Inner.GetUsersAsync(cancellationToken);
}

/// <summary>Returns a fixed, in-memory seed-user list.</summary>
public sealed class StubSeedUserSource : ISeedUserSource
{
    private readonly IReadOnlyList<SeedUserRecord> _users;

    public StubSeedUserSource(IReadOnlyList<SeedUserRecord> users) => _users = users;

    public Task<IReadOnlyList<SeedUserRecord>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_users);
}

/// <summary>Fluent builders for small, readable directory snapshots.</summary>
public static class Snap
{
    public static DirectoryBu Bu(string code, string group = "Group A", string portfolio = "Portfolio 1", string? name = null) =>
        new() { Code = code, Name = name ?? $"{code} BU", Group = group, Portfolio = portfolio };

    public static DirectoryPerson Person(
        string externalRef,
        string buCode,
        string? managerExternalRef = null,
        string employeeType = "Employee",
        bool isActive = true,
        bool onLeave = false,
        string? name = null,
        string? email = null,
        string? jobTitle = null) =>
        new()
        {
            ExternalRef = externalRef,
            Name = name ?? externalRef,
            Email = email ?? $"{externalRef}@synth.local".ToLowerInvariant(),
            JobTitle = jobTitle ?? "Engineer",
            ManagerExternalRef = managerExternalRef,
            BuCode = buCode,
            EmployeeType = employeeType,
            IsActive = isActive,
            OnLeave = onLeave,
        };

    public static DirectorySnapshot Of(IEnumerable<DirectoryBu> bus, IEnumerable<DirectoryPerson> persons) =>
        new() { Bus = bus.ToList(), Persons = persons.ToList() };

    /// <summary>A seed-user record for an already-synced <see cref="Person"/> (matched by
    /// <see cref="Person"/>) — lets a test sign in as an ordinary directory fixture (e.g. "LEAD")
    /// without needing the full canonical seed-users file. <paramref name="role"/> only matters when
    /// it names an explicit-grant label ("Platform Admin"/"HIG Executive" — see
    /// <c>LocalDevProvider</c>/QUESTIONS.md Q-013); any other value is treated as a plain
    /// hierarchy-tier label with no side effect.</summary>
    public static SeedUserRecord SeedUser(string externalRef, string role = "Manager", string buCode = "BU01") =>
        new()
        {
            Role = role,
            ExternalRef = externalRef,
            Name = externalRef,
            Email = $"{externalRef}@synth.local".ToLowerInvariant(),
            BuCode = buCode,
        };
}
