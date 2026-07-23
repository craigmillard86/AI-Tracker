using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-12 audit-completeness sweep — the SC-005 proof (contracts/api.md contract test 3): every wired
/// <b>[A]</b>-marked individual-read endpoint writes exactly ONE <see cref="AuditAction.IndividualView"/>
/// row per authorised call (correct actor/subject) and writes ZERO audit rows AND returns zero data on an
/// unauthorised call. Structured as a data-driven sweep over the [A] endpoint set, plus two route-table
/// guards that keep the sweep honest as the surface grows:
/// <list type="bullet">
/// <item>the set of individual-read routes in the running app equals the sweep's known list — a future
/// story adding e.g. <c>GET /api/bus/{buId}/people/{personId}/assessment</c> (contract [A], not yet built)
/// fails this test until the sweep is extended to cover it;</item>
/// <item>no audit-MUTATION endpoint exists anywhere (the log is append-only, FR-053) — asserted against the
/// live route table, complementing the code-level <c>AuditAppendOnlyTests</c>.</item>
/// </list>
/// </summary>
[Collection("hap-db")]
public sealed class AuditCompletenessSweepTests
{
    private readonly HapApiFactory _factory;

    public AuditCompletenessSweepTests(HapApiFactory factory) => _factory = factory;

    /// <summary>One wired [A] individual-read endpoint and how to exercise it: the authorised caller (writes
    /// one audit row), the subject read, and the callers who must be denied (404, zero audit).</summary>
    private sealed record AEndpoint(
        string Description,
        Func<AuditCompletenessSweepTests, Guid, string> UrlForSubject, // (self, subjectId) → request URL
        string AuthorisedCaller,
        string SubjectRef,
        string[] UnauthorisedCallers);

    // The wired [A] surface today: exactly one endpoint (HAP-9's team member read). The BU-lead individual
    // read (contract [A] GET /api/bus/{buId}/people/{personId}/assessment) is NOT yet implemented by any
    // shipped story — when it lands, add it here and the RouteTable guard below stops failing.
    private static readonly AEndpoint[] WiredAEndpoints =
    {
        new(
            "GET /api/team/members/{personId}/assessment",
            static (_, subjectId) => $"/api/team/members/{subjectId}/assessment",
            AuthorisedCaller: "MGR1",
            SubjectRef: "EMP1",
            UnauthorisedCallers: new[] { "MGR2", "EMP2", "ADMIN" }),
    };

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Every_wired_A_endpoint_audits_exactly_once_when_authorised_and_never_when_denied()
    {
        foreach (var endpoint in WiredAEndpoints)
        {
            // Fresh fixture (clean audit table) per endpoint, so the counts below are unambiguous.
            await SeedAsync();

            var subjectId = await PersonIdAsync(endpoint.SubjectRef);
            var authorisedId = await PersonIdAsync(endpoint.AuthorisedCaller);

            // --- authorised: exactly one IndividualView row, correct actor + subject ---
            var authorised = await ClientAsync(endpoint.AuthorisedCaller);
            var ok = await authorised.GetAsync(endpoint.UrlForSubject(this, subjectId));
            Assert.True(ok.StatusCode == HttpStatusCode.OK, $"{endpoint.Description}: authorised call should be 200");
            Assert.Equal(1, await IndividualViewCountAsync(subjectId));
            using (var scope = _factory.NewScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
                var row = await db.AuditLogs.SingleAsync(a =>
                    a.Action == AuditAction.IndividualView && a.SubjectPersonId == subjectId);
                Assert.Equal(authorisedId, row.ActorPersonId);
            }

            // --- unauthorised: 404 (existence-leak) AND no audit row added ---
            foreach (var attacker in endpoint.UnauthorisedCallers)
            {
                var before = await IndividualViewCountAsync(subjectId);
                var client = await ClientAsync(attacker);
                var denied = await client.GetAsync(endpoint.UrlForSubject(this, subjectId));
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                Assert.Equal(before, await IndividualViewCountAsync(subjectId)); // no new audit trace
            }
        }
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void The_route_table_individual_read_surface_equals_the_swept_set()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        // An [A] individual read is any route that addresses a SPECIFIC person's assessment. Rather than
        // trip on one naming shape (.../members/{...}/assessment), match structurally: the route mentions
        // "assessment" AND carries a person-ish route parameter ({personId}/{userId}/{memberId}/{subjectId}).
        // This catches a future person-addressed read regardless of its path noun (the naming assumption is
        // also recorded in contracts/api.md so the convention is load-bearing-by-record).
        var personParam = new Regex(@"\{[^}]*(person|user|member|subject)[^}]*\}", RegexOptions.IgnoreCase);

        var individualReadRoutes = source.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(raw => raw.Contains("assessment", StringComparison.OrdinalIgnoreCase) && personParam.IsMatch(raw))
            .Distinct()
            .ToList();

        // The route table's individual-read surface must EQUAL the swept set. If a new person-addressed
        // assessment read is wired, this fails until WiredAEndpoints is extended to audit-test it.
        Assert.Equal(WiredAEndpoints.Length, individualReadRoutes.Count);
        Assert.Contains(individualReadRoutes, r => r.Contains("/team/members/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void No_audit_mutation_endpoint_exists_in_the_route_table()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        var mutating = new[] { "POST", "PUT", "DELETE", "PATCH" };

        var offenders = source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("audit", StringComparison.OrdinalIgnoreCase))
            .Select(e => new
            {
                Pattern = e.RoutePattern.RawText ?? string.Empty,
                Methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>(),
            })
            .Where(x => x.Methods.Any(m => mutating.Contains(m, StringComparer.OrdinalIgnoreCase)))
            .Select(x => $"{string.Join("/", x.Methods)} {x.Pattern}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "The audit log is append-only — no write/update/delete audit endpoint may exist. Found: " +
            string.Join(", ", offenders));
    }

    // --- helpers -----------------------------------------------------------------------------------

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("MGR2", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("EMP2", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("EMP3", "BU01", managerExternalRef: "MGR2"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("MGR2", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("EMP2", role: "Individual"),
            Snap.SeedUser("EMP3", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var seed = await (await admin.PostAsync("/api/admin/frameworks", null)).Content.ReadFromJsonAsync<FrameworkSeedResult>();
        // Open a cycle and give EMP1 a submitted assessment (the subject of the [A] read).
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(seed!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        await SubmitSelfAsync("EMP1", 2);
        return admin;
    }

    // (helper block continues below)

    private async Task SubmitSelfAsync(string externalRef, int score)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<int> IndividualViewCountAsync(Guid subjectPersonId)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return await db.AuditLogs.CountAsync(a =>
            a.Action == AuditAction.IndividualView && a.SubjectPersonId == subjectPersonId);
    }
}
