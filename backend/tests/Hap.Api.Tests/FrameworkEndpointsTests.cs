using System.Net;
using System.Net.Http.Json;
using System.Threading;
using Hap.Domain.Frameworks;
using Hap.Infrastructure;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-6 acceptance criteria against the live seeded framework
/// (<c>docs/frameworks/ai-maturity-sdlc.v1.json</c>): seed idempotency, content equality against
/// the JSON, `GET /api/frameworks/current` shape/order, and the versioning invariant (a new
/// draft version never disturbs the active one). FR-054 immutability itself is a domain-level
/// guard test (Hap.Domain.Tests.FrameworkEntityTests) — nothing in this story's scope writes to
/// a locked version over HTTP, so there is no integration-level equivalent to exercise.
/// </summary>
[Collection("hap-db")]
public sealed class FrameworkEndpointsTests
{
    private readonly HapApiFactory _factory;

    public FrameworkEndpointsTests(HapApiFactory factory) => _factory = factory;

    private static readonly string DefinitionPath = FrameworkDefinitionLocator.ResolveDefaultPath();

    [Fact]
    public async Task Seed_loads_the_json_into_seven_dimensions_and_twenty_eight_descriptors()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/admin/frameworks", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FrameworkSeedResult>();
        Assert.NotNull(result);
        Assert.True(result!.VersionCreated);
        Assert.Equal(7, result.DimensionsCreated);
        Assert.Equal(28, result.DescriptorsCreated);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.Frameworks.CountAsync());
        Assert.Equal(1, await db.FrameworkVersions.CountAsync());
        Assert.Equal(7, await db.Dimensions.CountAsync());
        Assert.Equal(28, await db.LevelDescriptors.CountAsync());
    }

    [Fact]
    public async Task Seed_is_idempotent_reseeding_produces_no_duplicates()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        await client.PostAsync("/api/admin/frameworks", content: null);
        var secondResponse = await client.PostAsync("/api/admin/frameworks", content: null);
        var second = await secondResponse.Content.ReadFromJsonAsync<FrameworkSeedResult>();

        Assert.False(second!.VersionCreated);
        Assert.Equal(0, second.DimensionsCreated);
        Assert.Equal(0, second.DescriptorsCreated);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(1, await db.Frameworks.CountAsync());
        Assert.Equal(1, await db.FrameworkVersions.CountAsync());
        Assert.Equal(7, await db.Dimensions.CountAsync());
        Assert.Equal(28, await db.LevelDescriptors.CountAsync());
    }

    [Fact]
    public async Task Seeded_version_is_bootstrap_activated_so_current_has_something_to_serve()
    {
        // Q-011 (provisional): the JSON's own "status": "draft" is authoring metadata, not the
        // initial runtime FrameworkVersion.Status — the very first version for a framework is
        // auto-activated so GET /api/frameworks/current is functional immediately after seed.
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        await client.PostAsync("/api/admin/frameworks", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var version = await db.FrameworkVersions.SingleAsync();
        Assert.Equal(FrameworkVersionStatus.Active, version.Status);
        Assert.False(version.IsLocked);
    }

    [Fact]
    public async Task GetCurrent_returns_active_version_content_matching_the_json_in_order()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await client.PostAsync("/api/admin/frameworks", content: null);

        var response = await client.GetAsync("/api/frameworks/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.Content.ReadFromJsonAsync<FrameworkCurrentResponse>();
        Assert.NotNull(current);

        var expected = await FrameworkSeeder.LoadDefinitionAsync(DefinitionPath, CancellationToken.None);

        Assert.Equal(expected.Key, current!.FrameworkKey);
        Assert.Equal(expected.Framework, current.FrameworkName);
        Assert.Equal(expected.Version, current.VersionNumber);

        Assert.Equal(expected.Dimensions.Count, current.Dimensions.Count);
        var expectedLevelNames = expected.Levels.ToDictionary(l => l.Level, l => l.Name);

        for (var i = 0; i < expected.Dimensions.Count; i++)
        {
            var expectedDim = expected.Dimensions[i];
            var actualDim = current.Dimensions[i]; // order asserted positionally — display order preserved

            Assert.Equal(expectedDim.Key, actualDim.Key);
            Assert.Equal(expectedDim.Name, actualDim.Name);
            Assert.Equal(i, actualDim.DisplayOrder);

            var expectedLevels = expectedDim.Descriptors
                .OrderBy(kv => int.Parse(kv.Key))
                .Select(kv => (Level: int.Parse(kv.Key), Text: kv.Value))
                .ToList();

            Assert.Equal(expectedLevels.Count, actualDim.Levels.Count);
            for (var l = 0; l < expectedLevels.Count; l++)
            {
                Assert.Equal(expectedLevels[l].Level, actualDim.Levels[l].Level);
                Assert.Equal(expectedLevelNames[expectedLevels[l].Level], actualDim.Levels[l].LevelName);
                Assert.Equal(expectedLevels[l].Text, actualDim.Levels[l].DescriptorText);
            }
        }
    }

    [Fact]
    public async Task GetCurrent_returns_404_when_no_version_is_active_yet()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/frameworks/current");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_draft_v2_leaves_v1_untouched_and_current_still_serves_v1()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await client.PostAsync("/api/admin/frameworks", content: null);

        var beforeCurrent = await (await client.GetAsync("/api/frameworks/current"))
            .Content.ReadFromJsonAsync<FrameworkCurrentResponse>();

        using (var scope = _factory.NewScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<FrameworkAdminService>();
            var draft = await admin.CreateDraftVersionAsync("ai-maturity-sdlc", sourceRef: "test");

            Assert.Equal(2, draft.VersionNumber);
            Assert.Equal(FrameworkVersionStatus.Draft, draft.Status);
            Assert.False(draft.IsLocked);

            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(0, await db.Dimensions.CountAsync(d => d.FrameworkVersionId == draft.Id));
        }

        // v1's own dimensions/descriptors are untouched.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            Assert.Equal(7, await db.Dimensions.CountAsync());
            Assert.Equal(28, await db.LevelDescriptors.CountAsync());
        }

        var afterCurrent = await (await client.GetAsync("/api/frameworks/current"))
            .Content.ReadFromJsonAsync<FrameworkCurrentResponse>();

        Assert.Equal(1, afterCurrent!.VersionNumber); // still v1 — v2 never auto-activates
        Assert.Equal(beforeCurrent!.Dimensions.Count, afterCurrent.Dimensions.Count);
    }

    [Fact]
    public async Task Admin_list_returns_the_framework_with_its_version_and_lock_status()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await client.PostAsync("/api/admin/frameworks", content: null);

        var response = await client.GetAsync("/api/admin/frameworks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<AdminFrameworkResponse>>();
        Assert.NotNull(list);
        var framework = Assert.Single(list!);
        Assert.Equal("ai-maturity-sdlc", framework.Key);

        var version = Assert.Single(framework.Versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(nameof(FrameworkVersionStatus.Active), version.Status);
        Assert.False(version.IsLocked);
    }
}
