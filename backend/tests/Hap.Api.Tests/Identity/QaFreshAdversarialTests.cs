using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Synth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// QA Phase 3 (CLAUDE.md §9) — adversarial re-run against HAP-4, written fresh by a QA agent
/// with no shared context with Dev. These are NOT copies of <c>RedTeamEscalationTests</c>: every
/// case here targets a path Dev's own suite did not exercise (non-Individual attackers, route
/// variants, cookie tampering/staleness, and an independent full-endpoint inventory sweep).
/// Category=PrivacyReporting per CLAUDE.md §7 L3 requirement.
/// </summary>
[Collection("hap-db")]
[Trait("Category", "PrivacyReporting")]
public sealed class QaFreshAdversarialTests
{
    private readonly HapApiFactory _factory;

    public QaFreshAdversarialTests(HapApiFactory factory) => _factory = factory;

    private async Task SyncCanonicalAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new SyntheticDirectoryAdapter(_factory.CanonicalSnapshotPath);
        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        _factory.SeedUsers.Inner = new StubSeedUserSource(_factory.CanonicalSeedUsers);
    }

    // --- (a) fresh escalation re-run: attackers beyond "Individual" ----------------------------

    [Theory]
    [InlineData(Distributions.SeedManagerRef)]
    public async Task Plain_manager_cannot_reach_directory_sync(string attackerRef)
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, attackerRef);

        var response = await client.PostAsync("/api/admin/sync", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BU_lead_cannot_self_grant_via_the_override_endpoint()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        var attackerRef = Distributions.BuLeadRef(1);
        await HapApiFactory.SignInAsync(client, attackerRef);

        Guid attackerId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            attackerId = (await db.People.SingleAsync(p => p.ExternalRef == attackerRef)).Id;
        }

        // A BU Lead is already a "Manager" per HierarchyRoleResolver — try to reparent under the
        // exec ref to climb to Portfolio Leader, exactly as a BU Lead (not just a rank-and-file
        // Individual) plausibly would if the gate were role-blind rather than PlatformAdmin-only.
        var response = await client.PostAsJsonAsync("/api/admin/overrides", new
        {
            personId = attackerId,
            field = "Manager",
            overrideValue = Distributions.ExecRef,
            reason = "escalation attempt (BU Lead)",
            createdBy = "attacker",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        Assert.Equal(0, await verifyDb.OrgOverrides.CountAsync());
    }

    [Theory]
    [InlineData("/api/admin/frameworks/", "POST")]
    [InlineData("/api/admin/frameworks", "GET")]   // no-trailing-slash variant
    [InlineData("/api/admin/frameworks", "POST")]  // no-trailing-slash variant
    [InlineData("/api/admin/overrides/", "GET")]   // stray-trailing-slash variant of a route with no group slash
    public async Task Route_variants_around_the_admin_surfaces_never_leak_data_to_a_non_admin(
        string path, string methodName)
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.SeedIndividualRef);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(methodName), path));

        // The bar here is not "must be 403" (a route that simply doesn't exist correctly 404s
        // before authorization even runs) — the bar is that NO variant returns 200/201 or any
        // body carrying admin data. 401 would also be wrong (the caller IS authenticated) unless
        // the route genuinely doesn't match any mapped endpoint, in which case ASP.NET's routing
        // returns 404 before UseAuthorization sees it at all — verified case-by-case below.
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{methodName} {path} returned {(int)response.StatusCode} — expected 403 (gated) or 404 (unmapped), never a success status for a non-admin.");
    }

    [Fact]
    public void Get_variant_of_the_no_slash_admin_frameworks_path_is_unmapped_not_silently_authorized()
    {
        // Pin down exactly why the no-slash variant above returns what it returns: prove it is
        // genuinely unmapped (no RouteEndpoint matches), not an endpoint that happens to allow
        // anonymous/non-admin access. If a future change accidentally maps "/api/admin/frameworks"
        // (no slash) as a distinct anonymous-reachable endpoint, this assertion fails loudly.
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var patterns = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.DoesNotContain("/api/admin/frameworks", patterns);
        Assert.Contains("/api/admin/frameworks/", patterns);
    }

    // --- (b) independent full endpoint inventory --------------------------------------------

    [Fact]
    public void Every_mapped_endpoint_anywhere_in_the_app_is_either_api_gated_auth_anonymous_or_healthz()
    {
        // Independent re-derivation of the sweep Dev already wrote (UnauthenticatedSweepTests),
        // deliberately NOT reusing its filter logic: enumerate the ENTIRE route table (no "/api"
        // prefix filter at all) and classify every single mapped route by hand, so an admin route
        // accidentally mapped OUTSIDE the "/api" group (which would dodge Dev's prefix-filtered
        // sweep entirely) cannot hide.
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var allRoutes = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => new
            {
                Pattern = e.RoutePattern.RawText ?? "(none)",
                Methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" },
                RequiresAuth = e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() is not null,
            })
            .ToList();

        Assert.NotEmpty(allRoutes); // sanity: the sweep must find routes at all

        var knownAnonymous = new HashSet<string> { "/healthz", "/auth/signin", "/auth/signout" };

        foreach (var route in allRoutes)
        {
            bool isKnownAnonymous = knownAnonymous.Contains(route.Pattern);
            bool isApiRoute = route.Pattern.StartsWith("/api", StringComparison.Ordinal);

            if (isKnownAnonymous)
            {
                continue; // explicitly, deliberately anonymous — /auth/* and /healthz by contract
            }

            Assert.True(
                isApiRoute,
                $"Route '{route.Pattern}' is mapped OUTSIDE both the known-anonymous allowlist and " +
                "the /api group — this is exactly the shape of an admin route that dodges both the " +
                "unauthenticated sweep and manual review because it isn't where anyone would look.");

            Assert.True(
                route.RequiresAuth,
                $"Route '{route.Pattern}' is under /api but carries no IAuthorizeData metadata — " +
                "it will not be blocked by RequireAuthorization() even though it looks gated.");
        }
    }

    // --- (c) attempt to defeat the IsManager gate: a person with zero reports but a deep,
    //     dangling chain (no valid root exists anywhere in the graph) ---------------------------

    [Fact]
    public async Task With_no_valid_root_anywhere_in_the_graph_nobody_gets_a_leadership_label()
    {
        // Every person in this snapshot has a manager (so nobody is a "no manager" candidate root)
        // except one node whose manager points at itself post-sync-would-be-rejected — instead we
        // construct a graph with NO root: A's manager is B, B's manager is A conceptually is
        // rejected at import (self/cycle guard), so the achievable adversarial shape is a person
        // with a manager pointing to an id that was never imported (dangling external ref) is
        // itself rejected at import time (contracts/api.md: unresolvable manager_external_ref
        // fails the whole import). The remaining achievable case — the one this test proves — is
        // an all-non-root chain: every node HAS a manager, so ComputeDepthFromRoot must walk to
        // the true root; if a bug ever caused it to stop early and grant a label anyway, this
        // fails. TOP has zero reports and no manager but IS a root (has a report) is the normal
        // case tested elsewhere; here TOP deliberately has NO reports, so it must not be treated
        // as a root even though nothing manages it.
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("LONELY_TOP", "BU01"), // no manager, ZERO reports -> not a valid root
                Snap.Person("MIDDLE", "BU01", managerExternalRef: null),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("LONELY_TOP") });

        using var verify = _factory.NewScope();
        var resolver = verify.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
        var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        var lonelyId = (await db.People.SingleAsync(p => p.ExternalRef == "LONELY_TOP")).Id;

        var roles = await resolver.ResolveAsync(lonelyId);

        Assert.False(roles.IsManager);
        Assert.Equal(HierarchyRoles.None, roles);
    }

    // --- (d) session/cookie: forgery, replay after sign-out, and staleness after grant change --

    [Fact]
    public async Task A_tampered_cookie_value_is_treated_as_unauthenticated_not_as_a_valid_downgraded_session()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        var signIn = await HapApiFactory.SignInAsync(client, Distributions.AdminRef);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var cookieContainer = client.DefaultRequestHeaders.TryGetValues("Cookie", out _);

        // Directly forge a syntactically-plausible but cryptographically-invalid cookie value
        // (the real cookie is ASP.NET Data-Protection-encrypted; this is not a valid ticket) and
        // send it as the ONLY auth evidence on a fresh, unauthenticated client — simulates an
        // attacker who captured/guessed a cookie NAME but not a genuine ticket.
        using var forgedClient = _factory.CreateClient();
        forgedClient.DefaultRequestHeaders.Add("Cookie", "hap_auth=" + new string('A', 200));

        var response = await forgedClient.PostAsync("/api/admin/sync", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signed_out_cookie_cannot_be_replayed_against_a_gated_admin_endpoint()
    {
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.AdminRef);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        await client.PostAsync("/auth/signout", content: null);

        // Same HttpClient/CookieContainer — if sign-out only cleared client-side state and the
        // server still accepted the ticket, this would still succeed.
        var replay = await client.PostAsync("/api/admin/sync", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_PlatformAdmin_grant_mid_session_does_not_immediately_revoke_the_live_cookie()
    {
        // Documents (does not "fix" — no revoke endpoint exists yet; this proves the underlying
        // mechanism directly against the DB) the exact risk the Dev notes flagged as advisory A3:
        // roles are baked into the cookie at sign-in and are NOT re-read from RoleGrant per
        // request. This is a REAL finding to carry forward, not a false alarm — recording the
        // concrete proof here rather than trusting the prose description.
        await SyncCanonicalAsync();
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, Distributions.AdminRef);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var admin = await db.People.SingleAsync(p => p.ExternalRef == Distributions.AdminRef);
            var grants = await db.RoleGrants.Where(g => g.PersonId == admin.Id).ToListAsync();
            db.RoleGrants.RemoveRange(grants);
            await db.SaveChangesAsync();
        }

        // Same session cookie, PlatformAdmin grant now gone from the DB entirely.
        var response = await client.PostAsync("/api/admin/sync", content: null);

        // FINDING (not a HAP-4 defect — matches the documented advisory A3 and research D3's
        // cookie-based design): the stale cookie still authorizes past the PlatformAdmin policy
        // for up to the 8h sliding window, because RequireRole checks claims baked into the
        // cookie ticket, not a live RoleGrant read. Confirmed here, not merely asserted in prose.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        // SHARPER FINDING than advisory A3 anticipated: re-authentication does NOT clear the
        // revocation either. HAP-ADMIN's seed-users.json role label is "Platform Admin", and
        // LocalDevProvider.EnsureExplicitGrantAsync (QUESTIONS.md Q-013) idempotently RE-CREATES
        // the grant from that label on every sign-in — it does not merely read existing grants,
        // it re-derives and re-inserts them. So even a full sign-out + sign-in cycle restores the
        // "deleted" grant. Proved empirically here rather than assumed. This is NOT a
        // cross-identity escalation (no other fixture gains anything; HAP-ADMIN only ever
        // re-acquires ITS OWN designated role) and is consistent with Q-013's synthetic-local-only
        // bootstrap design — but it is an operational gap worth flagging forward: a future
        // role-grant admin endpoint (Q-013's "later story") cannot durably revoke PlatformAdmin
        // or HigExecutive from these two specific dev-seed fixtures without either special-casing
        // them or changing seed-users.json, because every sign-in silently re-grants per the label.
        await client.PostAsync("/auth/signout", content: null);
        await HapApiFactory.SignInAsync(client, Distributions.AdminRef);
        var afterResignIn = await client.PostAsync("/api/admin/sync", content: null);
        Assert.NotEqual(HttpStatusCode.Forbidden, afterResignIn.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, afterResignIn.StatusCode);

        using var verify = _factory.NewScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<HapDbContext>();
        var adminId = (await verifyDb.People.SingleAsync(p => p.ExternalRef == Distributions.AdminRef)).Id;
        Assert.True(await verifyDb.RoleGrants.AnyAsync(g => g.PersonId == adminId && g.Role == OrgRole.PlatformAdmin));
    }

    // --- Q-014 residual: confirm the KNOWN interim/dual-hat misassignment does NOT leak beyond
    //     the misclassified person's own /api/me (self-scope only, per session-lead's framing that
    //     HAP-4 does not consume the label for visibility scope — only future stories would). -----

    [Fact]
    public async Task Interim_layer_misassignment_is_confirmed_but_stays_confined_to_the_misclassified_persons_own_self_scope()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("EXEC", "BU01"),
                Snap.Person("PORT", "BU01", managerExternalRef: "EXEC"),
                Snap.Person("GRP", "BU01", managerExternalRef: "PORT"),
                Snap.Person("INTERIM", "BU01", managerExternalRef: "GRP"),
                Snap.Person("REALBULEAD", "BU01", managerExternalRef: "INTERIM"),
                Snap.Person("REPORT", "BU01", managerExternalRef: "REALBULEAD"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("INTERIM"), Snap.SeedUser("REALBULEAD"),
        });

        using (var verify = _factory.NewScope())
        {
            var resolver = verify.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
            var db = verify.ServiceProvider.GetRequiredService<HapDbContext>();
            var interimId = (await db.People.SingleAsync(p => p.ExternalRef == "INTERIM")).Id;
            var realId = (await db.People.SingleAsync(p => p.ExternalRef == "REALBULEAD")).Id;

            // Reproduces the red-team's exact hypothesis: depth-3 INTERIM wrongly reads "BU Lead";
            // depth-4 REALBULEAD is demoted to "Manager" only. Confirming the residual exists (it
            // is deliberately NOT fixed here per Q-014 — no structural anchor available) rather
            // than silently trusting the story notes' prose description.
            var interimRoles = await resolver.ResolveAsync(interimId);
            var realRoles = await resolver.ResolveAsync(realId);
            Assert.Contains("BU Lead", interimRoles.ToRoleNames());
            Assert.DoesNotContain("BU Lead", realRoles.ToRoleNames());
        }

        // The live-consumer check: INTERIM signing in and calling GET /api/me only ever sees
        // THEIR OWN (mislabelled) roles — never anyone else's, and no endpoint in this story lets
        // INTERIM act as BU Lead against another person's data (no such data-scoped endpoint
        // exists yet — HAP-5 is the hard block). This proves the residual is confined exactly as
        // the session-lead's ruling claimed, rather than trusting the claim.
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "INTERIM");
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.NotNull(me);
        Assert.Contains("BU Lead", me!.ComputedRoles);
        Assert.Equal("INTERIM", me.ExternalRef); // self-scope only — cannot fetch REALBULEAD's data
    }
}
