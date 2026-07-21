using System.Security.Claims;
using Hap.Api.Identity;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// Contract-shape tests for the identity port (contracts/api.md "Ports — IIdentityProvider";
/// research D3). Two things this suite proves that nothing else does:
/// (1) the principal <c>SignInAsync</c> produces carries EXACTLY <c>person_id</c> + explicit
/// roles — the whole claim set, not just "at least these" — nothing hierarchy-derived, nothing
/// provider-specific, person matched by external_ref;
/// (2) the port is genuinely adapter-agnostic — a second, wholly independent implementation
/// compiles against <see cref="IIdentityProvider"/> with zero changes to the interface itself.
/// </summary>
[Collection("hap-db")]
public sealed class ContractShapeTests
{
    private readonly HapApiFactory _factory;

    public ContractShapeTests(HapApiFactory factory) => _factory = factory;

    /// <summary>Asserts the principal's ENTIRE claim set — not a subset — so a stray claim (the
    /// code-review finding this test originally missed: LocalDevProvider briefly carried an
    /// unused "external_ref" claim beyond the contract shape) fails loudly here.</summary>
    private static void AssertExactClaimSet(
        ClaimsPrincipal principal, string expectedPersonId, params string[] expectedRoles)
    {
        var actual = principal.Claims
            .Select(c => (c.Type, c.Value))
            .OrderBy(c => c.Type, StringComparer.Ordinal)
            .ThenBy(c => c.Value, StringComparer.Ordinal)
            .ToList();

        var expected = new List<(string Type, string Value)> { ("person_id", expectedPersonId) };
        expected.AddRange(expectedRoles.Select(r => (ClaimTypes.Role, r)));
        expected = expected.OrderBy(c => c.Type, StringComparer.Ordinal).ThenBy(c => c.Value, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Principal_carries_exactly_person_id_and_no_roles_person_matched_by_external_ref()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("PLAIN", "BU01") }));
        using (var seedScope = _factory.NewScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("PLAIN") });

        Guid personId;
        using (var lookupScope = _factory.NewScope())
        {
            var db = lookupScope.ServiceProvider.GetRequiredService<HapDbContext>();
            personId = (await db.People.SingleAsync(p => p.ExternalRef == "PLAIN")).Id;
        }

        using var scope = _factory.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<IIdentityProvider>();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var principal = await provider.SignInAsync(httpContext, "PLAIN");

        // Person matched by external_ref (contracts/api.md) — resolved to the real Person.Id.
        // PLAIN has no explicit RoleGrant, so the ONLY claim is person_id — critically, no
        // hierarchy-derived role either (those are computed separately, per request, by
        // HierarchyRoleResolver — never baked into the principal) and no other claim of any kind.
        AssertExactClaimSet(principal, personId.ToString());
    }

    [Fact]
    public async Task Principal_with_an_explicit_grant_carries_exactly_person_id_and_that_role_nothing_else()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("GRANTED", "BU01") }));
        using (var seedScope = _factory.NewScope())
        {
            await seedScope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        // "Platform Admin" is one of the two seed-role labels LocalDevProvider bootstraps an
        // explicit RoleGrant for at sign-in (QUESTIONS.md Q-013) — exercises the non-trivial path.
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("GRANTED", role: "Platform Admin") });

        Guid personId;
        using (var lookupScope = _factory.NewScope())
        {
            var db = lookupScope.ServiceProvider.GetRequiredService<HapDbContext>();
            personId = (await db.People.SingleAsync(p => p.ExternalRef == "GRANTED")).Id;
        }

        using var scope = _factory.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<IIdentityProvider>();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var principal = await provider.SignInAsync(httpContext, "GRANTED");

        AssertExactClaimSet(principal, personId.ToString(), "PlatformAdmin");
    }

    // Compile-time proof only (constitution Art. IX.4 / QUESTIONS.md): a second, wholly
    // independent IIdentityProvider implementation — sharing nothing with LocalDevProvider —
    // satisfies the interface untouched. Never registered, never exercised functionally; if this
    // class stops compiling, the port has picked up something LocalDevProvider-specific.
    private sealed class SecondHypotheticalAdapterStub : IIdentityProvider
    {
        public Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("compile-time contract proof only");

        public Task<ClaimsPrincipal> SignInAsync(
            HttpContext context, string userKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("compile-time contract proof only");

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("compile-time contract proof only");
    }

    [Fact]
    public void Port_accepts_a_second_independent_adapter_implementation()
    {
        IIdentityProvider secondAdapter = new SecondHypotheticalAdapterStub();
        Assert.IsAssignableFrom<IIdentityProvider>(secondAdapter);
    }
}
