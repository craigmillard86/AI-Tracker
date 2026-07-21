using Hap.Api.Identity;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests.Identity;

/// <summary>
/// Direct unit-style coverage of <see cref="HierarchyRoleResolver"/> (QUESTIONS.md Q-014). Builds a
/// small hand-crafted org shape that deliberately reproduces the two failure modes that sank the
/// simpler heuristics considered before landing on manager-chain depth:
/// <list type="bullet">
/// <item>the "BU01 collapse" — HIG Executive, Portfolio Leader, Group Leader, and BU Lead are all
/// homed in the SAME business unit, exactly like the real generator's BU01 (Distributions.cs), so
/// any home-BU-based heuristic cannot tell them apart;</item>
/// <item>a cross-BU direct report (the BU Lead manages someone homed in a different BU — mirrors
/// the generator's engineered edge (i), <c>CrossBuReportRef</c>) — proves the algorithm does not
/// need the BU Lead's subtree to be a clean, exclusive span of their own BU;</item>
/// <item>a null-manager, zero-report orphan (mirrors edge (e), <c>NullManagerRef</c>) — proves an
/// orphan is never mistaken for a root/HIG-Executive-shaped node.</item>
/// </list>
/// See also SignInFlowTests for the same rule exercised end-to-end against the real seven canonical
/// seed users via <c>GET /api/me</c>.
/// </summary>
[Collection("hap-db")]
public sealed class HierarchyRoleResolverTests
{
    private readonly HapApiFactory _factory;

    public HierarchyRoleResolverTests(HapApiFactory factory) => _factory = factory;

    private async Task SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("EXEC", "BU01"), // root: no manager, has a report
                Snap.Person("PORTLEAD", "BU01", managerExternalRef: "EXEC"),
                Snap.Person("GRPLEAD", "BU01", managerExternalRef: "PORTLEAD"),
                Snap.Person("BULEAD1", "BU01", managerExternalRef: "GRPLEAD"),
                Snap.Person("BULEAD2", "BU02", managerExternalRef: "GRPLEAD"),
                Snap.Person("MGR1", "BU01", managerExternalRef: "BULEAD1"),
                Snap.Person("IND1", "BU01", managerExternalRef: "MGR1"),
                // Cross-BU report: managed by BULEAD1 (home BU01) but homed in BU02.
                Snap.Person("CROSSREPORT", "BU02", managerExternalRef: "BULEAD1"),
                // Directory-gap orphan: no manager, zero reports — must never read as a root.
                Snap.Person("ORPHAN", "BU01"),
            }));

        using var scope = _factory.NewScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
    }

    private async Task<Guid> IdOfAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<HierarchyRoles> ResolveAsync(string externalRef)
    {
        var personId = await IdOfAsync(externalRef);
        using var scope = _factory.NewScope();
        var resolver = scope.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
        return await resolver.ResolveAsync(personId);
    }

    [Fact]
    public async Task Root_gets_Manager_only_HIG_Executive_is_not_hierarchy_derived()
    {
        await SeedAsync();
        var roles = await ResolveAsync("EXEC");

        Assert.True(roles.IsManager);
        Assert.Null(roles.PortfolioLeaderOfPortfolioId);
        Assert.Null(roles.GroupLeaderOfGroupId);
        Assert.Null(roles.BuLeadOfBusinessUnitId);
        Assert.Equal(new[] { "Manager" }, roles.ToRoleNames());
    }

    [Fact]
    public async Task Depth_one_is_Portfolio_Leader_of_their_own_BUs_portfolio()
    {
        await SeedAsync();
        var roles = await ResolveAsync("PORTLEAD");

        Assert.True(roles.IsManager);
        Assert.NotNull(roles.PortfolioLeaderOfPortfolioId);
        Assert.Null(roles.GroupLeaderOfGroupId);
        Assert.Null(roles.BuLeadOfBusinessUnitId);
        Assert.Equal(new[] { "Manager", "Portfolio Leader" }, roles.ToRoleNames());
    }

    // The core BU01-collapse regression: GRPLEAD is homed in the exact same BU (BU01) as EXEC,
    // PORTLEAD, and BULEAD1, yet must land as Group Leader — not Portfolio Leader or BU Lead —
    // purely from graph depth, never from which BU their person row happens to sit in.
    [Fact]
    public async Task Depth_two_is_Group_Leader_even_though_homed_in_the_same_BU_as_every_other_leadership_tier()
    {
        await SeedAsync();
        var roles = await ResolveAsync("GRPLEAD");

        Assert.True(roles.IsManager);
        Assert.Null(roles.PortfolioLeaderOfPortfolioId);
        Assert.NotNull(roles.GroupLeaderOfGroupId);
        Assert.Null(roles.BuLeadOfBusinessUnitId);
        Assert.Equal(new[] { "Manager", "Group Leader" }, roles.ToRoleNames());
    }

    // The cross-BU-report regression: BULEAD1 manages CROSSREPORT (homed in BU02), yet is still
    // recognised as BU Lead of their OWN home BU (BU01) — a span/coverage-based rule would not
    // survive this (BULEAD1's subtree would leak into BU02).
    [Fact]
    public async Task Depth_three_is_BU_Lead_of_their_own_home_BU_even_with_a_cross_BU_direct_report()
    {
        await SeedAsync();
        var roles = await ResolveAsync("BULEAD1");

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var bu01 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU01");

        Assert.True(roles.IsManager);
        Assert.Null(roles.PortfolioLeaderOfPortfolioId);
        Assert.Null(roles.GroupLeaderOfGroupId);
        Assert.Equal(bu01.Id, roles.BuLeadOfBusinessUnitId);
        Assert.Equal(new[] { "Manager", "BU Lead" }, roles.ToRoleNames());
    }

    // L3 code-review finding: BULEAD2 sits at the exact same graph depth (3) as BULEAD1 but has
    // zero active reports of its own — a since-vacated leadership slot. It must NOT carry the "BU
    // Lead" label (or any leadership label): depth alone is not sufficient, isManager gates it too.
    [Fact]
    public async Task Depth_three_with_zero_active_reports_gets_no_leadership_label()
    {
        await SeedAsync();
        var roles = await ResolveAsync("BULEAD2");

        Assert.False(roles.IsManager);
        Assert.Null(roles.BuLeadOfBusinessUnitId);
        Assert.Null(roles.GroupLeaderOfGroupId);
        Assert.Null(roles.PortfolioLeaderOfPortfolioId);
        Assert.Equal(Array.Empty<string>(), roles.ToRoleNames());
    }

    [Fact]
    public async Task An_ordinary_manager_below_depth_three_gets_Manager_only()
    {
        await SeedAsync();
        var roles = await ResolveAsync("MGR1");

        Assert.True(roles.IsManager);
        Assert.Equal(new[] { "Manager" }, roles.ToRoleNames());
    }

    [Fact]
    public async Task An_individual_with_no_reports_gets_no_role_names()
    {
        await SeedAsync();
        var roles = await ResolveAsync("IND1");

        Assert.False(roles.IsManager);
        Assert.Equal(Array.Empty<string>(), roles.ToRoleNames());
    }

    // The cross-BU report itself: a leaf, unaffected by which BU it is homed in versus its manager.
    [Fact]
    public async Task Cross_BU_report_itself_is_a_plain_individual()
    {
        await SeedAsync();
        var roles = await ResolveAsync("CROSSREPORT");

        Assert.False(roles.IsManager);
        Assert.Equal(Array.Empty<string>(), roles.ToRoleNames());
    }

    // The directory-gap regression: a null-manager, zero-report person must never be mistaken for
    // a root (which would wrongly make every downstream depth computation shift by one).
    [Fact]
    public async Task Null_manager_orphan_with_no_reports_is_not_mistaken_for_a_root()
    {
        await SeedAsync();
        var roles = await ResolveAsync("ORPHAN");

        Assert.False(roles.IsManager);
        Assert.Equal(Array.Empty<string>(), roles.ToRoleNames());
    }

    [Fact]
    public async Task Deactivated_person_gets_the_baseline_no_hierarchy_role_snapshot()
    {
        await SeedAsync();
        var personId = await IdOfAsync("IND1");

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var person = await db.People.SingleAsync(p => p.Id == personId);
            // No public "deactivate a synced person via override" path exists yet — exercise the
            // domain method directly, matching how DirectoryImportService itself deactivates leavers.
            person.Deactivate();
            await db.SaveChangesAsync();
        }

        using var verify = _factory.NewScope();
        var resolver = verify.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
        var roles = await resolver.ResolveAsync(personId);

        Assert.Equal(HierarchyRoles.None, roles);
    }
}
