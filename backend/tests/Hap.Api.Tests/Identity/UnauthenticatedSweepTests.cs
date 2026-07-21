using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// AC4: unauthenticated requests to any <c>/api/*</c> endpoint return 401 — explicitly including
/// the HAP-3 admin endpoints (<c>POST /api/admin/sync</c>, <c>GET/POST /api/admin/overrides</c>)
/// and, after the HAP-4/HAP-6 rebase integration, HAP-6's framework endpoints
/// (<c>GET /api/frameworks/current</c>, <c>GET/POST /api/admin/frameworks</c>). This sweeps every
/// route the app actually has mapped under <c>/api</c> via <see cref="EndpointDataSource"/> rather
/// than hand-listing them, so a future endpoint added to the authorized group is covered
/// automatically.
/// </summary>
[Collection("hap-db")]
public sealed class UnauthenticatedSweepTests
{
    private readonly HapApiFactory _factory;

    public UnauthenticatedSweepTests(HapApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_mapped_api_route_returns_401_without_a_session()
    {
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var apiRoutes = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api", StringComparison.Ordinal))
            .SelectMany(e =>
            {
                var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    ?? new[] { "GET" };
                return methods.Select(m => (Pattern: e.RoutePattern.RawText!, Method: m));
            })
            .Distinct()
            .ToList();

        // Sanity: the sweep must actually find the routes it's meant to prove 401 for — an empty
        // or short list would make this test vacuously (and silently) pass.
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/admin/sync" && r.Method == "POST");
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/admin/overrides" && r.Method == "GET");
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/admin/overrides" && r.Method == "POST");
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/me" && r.Method == "GET");
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/frameworks/current" && r.Method == "GET");
        // Trailing slash: FrameworkEndpoints.cs maps "" under api.MapGroup("/admin/frameworks"),
        // which resolves to "/api/admin/frameworks/" (verified against the live route table —
        // ASP.NET's group + empty-string-route combination keeps the separator).
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/admin/frameworks/" && r.Method == "GET");
        Assert.Contains(apiRoutes, r => r.Pattern == "/api/admin/frameworks/" && r.Method == "POST");

        var client = _factory.CreateClient();
        foreach (var (pattern, method) in apiRoutes)
        {
            if (pattern.Contains('{'))
            {
                throw new InvalidOperationException(
                    $"route '{pattern}' has a route parameter — the sweep needs a concrete value " +
                    "substituted before it can be requested; extend this test rather than skip it.");
            }

            var request = new HttpRequestMessage(new HttpMethod(method), pattern);
            var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{method} {pattern} should return 401 for an unauthenticated caller (got {(int)response.StatusCode}).");
        }
    }
}
