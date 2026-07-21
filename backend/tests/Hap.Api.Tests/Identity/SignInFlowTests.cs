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
/// End-to-end sign-in flow tests (FR-055; contracts/api.md "Auth" + "Self scope"). Covers the
/// full acceptance-criteria list for HAP-4: the role picker lists seed data (not hard-coded), the
/// cookie round-trip, all seven seeded roles via <c>GET /api/me</c>, and the "promote a manager
/// link, no re-sign-in" requirement.
/// </summary>
[Collection("hap-db")]
public sealed class SignInFlowTests
{
    private readonly HapApiFactory _factory;

    public SignInFlowTests(HapApiFactory factory) => _factory = factory;

    private async Task SyncCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        // The factory defaults SeedUsers to the canonical list already, but a previous test in
        // this shared, non-parallelised collection may have swapped it — restore explicitly so
        // this test never depends on run order.
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);
    }

    // --- AC1: GET /auth/signin lists the seeded users from seed data, not hard-coded ----------
    [Fact]
    public async Task Auth_signin_lists_all_seven_seeded_roles_from_seed_data()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/signin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var users = await response.Content.ReadFromJsonAsync<List<SeedUserRecord>>();
        Assert.NotNull(users);

        Assert.Equal(
            _factory.CanonicalSeedUsers.Select(u => u.ExternalRef).OrderBy(x => x, StringComparer.Ordinal),
            users!.Select(u => u.ExternalRef).OrderBy(x => x, StringComparer.Ordinal));

        var expectedRoles = new[]
        {
            "Individual", "Manager", "BU Lead", "Group Leader", "Portfolio Leader", "HIG Executive", "Platform Admin",
        };
        Assert.Equal(
            expectedRoles.OrderBy(x => x, StringComparer.Ordinal),
            users.Select(u => u.Role).OrderBy(x => x, StringComparer.Ordinal));
    }

    // --- AC1: POST /auth/signin sets a cookie; it authenticates the very next request ----------
    [Fact]
    public async Task Post_auth_signin_sets_a_cookie_that_authenticates_subsequent_requests()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();

        var signIn = await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // --- AC1: POST /auth/signout clears the session --------------------------------------------
    [Fact]
    public async Task Post_auth_signout_clears_the_session()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        var signOut = await client.PostAsync("/auth/signout", content: null);
        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);

        var meAfter = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Sign_in_with_an_unknown_user_key_returns_400()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/signin", new SignInRequest("NOT-A-SEED-USER"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_before_the_directory_has_ever_synced_returns_409()
    {
        await _factory.ResetAsync(); // empty DB — nothing synced yet
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/signin", new SignInRequest(Distributions.SeedIndividualRef));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_as_a_deactivated_person_returns_400()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("GONE", "BU01", isActive: false) }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("GONE") });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/signin", new SignInRequest("GONE"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- AC4: GET /api/me returns profile + computed roles for each of the seven seeded users --
    [Theory]
    // (externalRefKey, explicitRolesCsv, computedRolesCsv) — externalRefKey resolved to the
    // canonical Distributions ref inside the test; CSVs are compared as sets (order-independent).
    [InlineData("Individual", "", "")]
    [InlineData("Manager", "", "Manager")]
    [InlineData("BuLead", "", "Manager,BU Lead")]
    [InlineData("GroupLeader", "", "Manager,Group Leader")]
    [InlineData("PortfolioLeader", "", "Manager,Portfolio Leader")]
    [InlineData("HigExecutive", "HigExecutive", "Manager")]
    [InlineData("PlatformAdmin", "PlatformAdmin", "")]
    public async Task Get_me_returns_profile_and_computed_roles_for_every_seeded_role(
        string roleKey, string expectedExplicitCsv, string expectedComputedCsv)
    {
        await SyncCanonicalAsync();

        string externalRef = roleKey switch
        {
            "Individual" => Distributions.SeedIndividualRef,
            "Manager" => Distributions.SeedManagerRef,
            "BuLead" => Distributions.BuLeadRef(1),
            "GroupLeader" => Distributions.GroupLeaderRef(1),
            "PortfolioLeader" => Distributions.PortfolioLeaderRef(1),
            "HigExecutive" => Distributions.ExecRef,
            "PlatformAdmin" => Distributions.AdminRef,
            _ => throw new ArgumentOutOfRangeException(nameof(roleKey)),
        };

        var client = _factory.CreateClient();
        var signIn = await HapApiFactory.SignInAsync(client, externalRef);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.NotNull(me);
        Assert.Equal(externalRef, me!.ExternalRef);

        var expectedExplicit = Split(expectedExplicitCsv);
        var expectedComputed = Split(expectedComputedCsv);

        Assert.Equal(expectedExplicit, me.ExplicitRoles.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(expectedComputed, me.ComputedRoles.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Split(string csv) =>
        csv.Length == 0
            ? Enumerable.Empty<string>()
            : csv.Split(',').OrderBy(x => x, StringComparer.Ordinal);

    // --- AC2: promoting a person's manager link changes their computed role, no re-sign-in -----
    [Fact]
    public async Task Promoting_a_persons_manager_link_changes_their_computed_role_without_re_sign_in()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[] { Snap.Person("BOSS", "BU01") }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("BOSS") });

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "BOSS");

        var before = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.DoesNotContain("Manager", before!.ComputedRoles);

        // Re-sync with BOSS now managing a report — same client/cookie, no re-sign-in.
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("BOSS", "BU01"),
                Snap.Person("REPORT", "BU01", managerExternalRef: "BOSS"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }

        var after = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Contains("Manager", after!.ComputedRoles);
    }

    // --- AC1 (dev-seed side effect, QUESTIONS.md Q-013): the explicit grant is idempotent -------
    [Fact]
    public async Task Signing_in_as_the_HIG_Executive_fixture_twice_does_not_duplicate_the_role_grant()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();

        await HapApiFactory.SignInAsync(client, Distributions.ExecRef);
        await client.PostAsync("/auth/signout", content: null);
        await HapApiFactory.SignInAsync(client, Distributions.ExecRef);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var exec = await db.People.SingleAsync(p => p.ExternalRef == Distributions.ExecRef);
        var grantCount = await db.RoleGrants.CountAsync(g => g.PersonId == exec.Id);
        Assert.Equal(1, grantCount);
    }
}
