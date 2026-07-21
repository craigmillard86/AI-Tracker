using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// L3 panel round 1 — hap-red-team's VIOLATION FOUND finding, closed in this story (session-lead
/// ruling: not deferred to HAP-5). Concrete escalation path proved on the pre-fix tip: an
/// authenticated Individual could call GET /auth/signin (anonymous, leaks every seeded
/// external_ref) then POST /api/admin/overrides — gated only by "any authenticated session" (401),
/// with no PlatformAdmin check — to reparent themselves under the org root (HAP-EXEC). After that,
/// HierarchyRoleResolver's depth-from-root rule would label the attacker "Portfolio Leader" (depth
/// 1), a leadership tier they never legitimately held. Latent today (no score/aggregate tables
/// exist yet to read with the elevated label), but unsafe for a future story to build visibility
/// scope on. Closed by requiring the <c>PlatformAdmin</c> policy on <c>AdminEndpoints</c>
/// (backend/src/Hap.Api/AdminEndpoints.cs) — see the story notes for the full panel history.
/// </summary>
[Collection("hap-db")]
public sealed class RedTeamEscalationTests
{
    private readonly HapApiFactory _factory;

    public RedTeamEscalationTests(HapApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Individual_cannot_self_escalate_to_Portfolio_Leader_via_the_override_endpoint()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);

        Guid individualId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            individualId = (await db.People.SingleAsync(p => p.ExternalRef == Distributions.SeedIndividualRef)).Id;
        }

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);

        // The attack: an ordinary Individual (no explicit grant) tries to reparent themselves
        // directly under the org root via the override endpoint they can already reach (401-gated,
        // not role-gated, before this story's fix).
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = individualId,
            field = "Manager",
            overrideValue = Distributions.ExecRef,
            reason = "escalation attempt",
            createdBy = "attacker",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // No Portfolio Leader after — the override was never applied, so the manager chain (and
        // therefore the computed hierarchy tier) is unchanged from the ordinary Individual it was.
        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await verifyDb.OrgOverrides.CountAsync());

        var individual = await verifyDb.People.SingleAsync(p => p.Id == individualId);
        var exec = await verifyDb.People.SingleAsync(p => p.ExternalRef == Distributions.ExecRef);
        Assert.NotEqual(exec.Id, individual.ManagerPersonId);

        var resolver = verify.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
        var roles = await resolver.ResolveAsync(individualId);
        Assert.DoesNotContain("Portfolio Leader", roles.ToRoleNames());
    }

    // Companion positive case: the same request succeeds (well, resolves past the auth gate — 201
    // or a domain-validation status, never 401/403) for an actual PlatformAdmin, proving the fix
    // is a role gate and not an accidental blanket lockout of the endpoint.
    [Fact]
    public async Task Platform_admin_is_not_blocked_by_the_same_gate()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);

        Guid individualId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            individualId = (await db.People.SingleAsync(p => p.ExternalRef == Distributions.SeedIndividualRef)).Id;
        }

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.AdminRef);

        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = individualId,
            field = "Manager",
            overrideValue = Distributions.ExecRef,
            reason = "legitimate admin correction",
            createdBy = "platform-admin",
        });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // HAP-6 rebase integration: the PlatformAdmin gate now covers THREE admin surfaces
    // (sync, overrides, frameworks) — session-lead's explicit instruction was to prove a non-admin
    // gets 403 on all of them, not just the one the original escalation targeted.
    [Fact]
    public async Task Non_admin_gets_403_on_every_platform_admin_surface_including_frameworks()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);

        var probes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Post, "/api/admin/sync"),
            (HttpMethod.Get, "/api/admin/overrides"),
            (HttpMethod.Post, "/api/admin/overrides"),
            (HttpMethod.Get, "/api/admin/frameworks/"),
            (HttpMethod.Post, "/api/admin/frameworks/"),
        };

        foreach (var (method, path) in probes)
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, path));
            Assert.True(
                response.StatusCode == HttpStatusCode.Forbidden,
                $"{method} {path} should return 403 for an authenticated non-admin (got {(int)response.StatusCode}).");
        }
    }

    // GET /api/frameworks/current stays at the blanket "any authenticated role" level (it drives
    // the assessment UI for everyone) — an ordinary Individual must NOT be blocked by the
    // PlatformAdmin gate that covers the three admin surfaces above.
    [Fact]
    public async Task Non_admin_can_read_frameworks_current_the_blanket_authenticated_level()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);

        var response = await client.GetAsync("/api/frameworks/current");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        // No framework was seeded in this test's minimal setup, so the handler correctly reports
        // 404 (FrameworkEndpointsTests covers the seeded/200 case) — the point here is only that
        // an ordinary Individual clears the auth gate to reach that handler at all.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
