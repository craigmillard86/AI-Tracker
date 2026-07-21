using System.Net;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// AC-6 confirmation: the Platform-Admin routes are gated across ALL SEVEN seeded roles. The gate
/// itself shipped with HAP-4 (AdminEndpoints + the PlatformAdmin policy); this parameterised sweep is
/// HAP-5's explicit closing of QUESTIONS.md Q-004 — every non-admin seeded role is denied (403), and
/// the Platform Admin is admitted. Category=PrivacyReporting.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class AdminGateAllRolesTests
{
    private readonly HapApiFactory _factory;

    public AdminGateAllRolesTests(HapApiFactory factory) => _factory = factory;

    private async Task SyncCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);
    }

    private string RefForRole(string role) =>
        _factory.CanonicalSeedUsers.Single(u => u.Role == role).ExternalRef;

    [Theory]
    [InlineData("Individual")]
    [InlineData("Manager")]
    [InlineData("BU Lead")]
    [InlineData("Group Leader")]
    [InlineData("Portfolio Leader")]
    [InlineData("HIG Executive")]
    public async Task Non_admin_seeded_roles_are_denied_the_admin_routes(string role)
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, RefForRole(role));

        // The whole [PA] matrix in one sweep: sync, overrides (GET+POST), frameworks (GET+POST). The
        // authorization gate fires before model binding, so a null body still yields 403 for non-admins.
        var syncPost = await client.PostAsync("/api/admin/sync", content: null);
        var overridesGet = await client.GetAsync("/api/admin/overrides");
        var overridesPost = await client.PostAsync("/api/admin/overrides", content: null);
        var frameworksGet = await client.GetAsync("/api/admin/frameworks");
        var frameworksPost = await client.PostAsync("/api/admin/frameworks", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, syncPost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, overridesGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, overridesPost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, frameworksGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, frameworksPost.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_is_admitted_to_the_admin_routes()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, RefForRole("Platform Admin"));

        var syncPost = await client.PostAsync("/api/admin/sync", content: null);
        var overridesGet = await client.GetAsync("/api/admin/overrides");
        var frameworksGet = await client.GetAsync("/api/admin/frameworks");

        // Admitted past the [PA] gate: never 403/401. GET reads return 200.
        Assert.NotEqual(HttpStatusCode.Forbidden, syncPost.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, syncPost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overridesGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, frameworksGet.StatusCode);
    }
}
