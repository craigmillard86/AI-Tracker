using Hap.Api.Authorization;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// Parameterised coverage of <see cref="RoleScope"/> against the spec "Users and roles" "Sees" column
/// (FR-022/FR-025). The load-bearing invariant — Group/Portfolio/Executive/Admin scopes carry NO
/// individual-read capability by construction — is asserted for every role. Category=PrivacyReporting.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class RoleScopeTests
{
    // A small fixed org: two portfolios, groups, and BUs, so group/portfolio expansion is observable.
    //   P1 { G1 { BU1, BU2 }, G2 { BU3 } }   P2 { G3 { BU4 } }
    private static GraphBuilder Org()
    {
        var b = new GraphBuilder()
            .Bu("BU1", "G1", "P1").Bu("BU2", "G1", "P1")
            .Bu("BU3", "G2", "P1")
            .Bu("BU4", "G3", "P2");
        b.Person("evp1", "BU1");
        b.Person("mgr", "BU1", manager: "evp1");
        b.Person("ind", "BU1", manager: "mgr");
        return b;
    }

    // --- the invariant that matters most: only Individual/Manager/BuLead may reach individuals -----

    [Theory]
    [InlineData(SeamRole.Individual, false)]
    [InlineData(SeamRole.Manager, true)]
    [InlineData(SeamRole.BuLead, true)]
    [InlineData(SeamRole.GroupLeader, false)]
    [InlineData(SeamRole.PortfolioLeader, false)]
    [InlineData(SeamRole.HigExecutive, false)]
    [InlineData(SeamRole.PlatformAdmin, false)]
    public void Only_within_BU_roles_may_reach_individual_data(SeamRole role, bool allowsIndividualRead)
    {
        var b = Org();
        var g = b.Build();
        var assignment = new RoleAssignment(
            role, b.Id("ind"),
            BusinessUnitId: b.Id("BU1"), GroupId: b.Id("G1"), PortfolioId: b.Id("P1"));

        var scope = RoleScope.For(assignment, g);

        Assert.Equal(allowsIndividualRead, scope.AllowsIndividualRead);

        // The above-BU roles and Platform Admin reach NO individual data at all (FR-025 clause 2);
        // the Individual role also has AllowsIndividualRead=false but keeps OwnOnly reach (own scores).
        if (role is SeamRole.GroupLeader or SeamRole.PortfolioLeader
            or SeamRole.HigExecutive or SeamRole.PlatformAdmin)
        {
            Assert.Equal(IndividualReadReach.None, scope.IndividualReadReach);
        }
    }

    // --- Group Leader sees exactly the BUs in their group ---------------------------------------

    [Fact]
    public void Group_leader_sees_every_BU_in_their_group_and_no_individual_read()
    {
        var b = Org();
        var g = b.Build();
        var scope = RoleScope.For(new RoleAssignment(SeamRole.GroupLeader, b.Id("evp1"), GroupId: b.Id("G1")), g);

        Assert.False(scope.AllowsIndividualRead);
        Assert.Equal(IndividualReadReach.None, scope.IndividualReadReach);
        Assert.Equal(new[] { b.Id("BU1"), b.Id("BU2") }.OrderBy(x => x),
            scope.AggregateBusinessUnitIds.OrderBy(x => x));
    }

    // --- Portfolio Leader sees every BU across their portfolio's groups -------------------------

    [Fact]
    public void Portfolio_leader_sees_every_BU_across_the_portfolio()
    {
        var b = Org();
        var g = b.Build();
        var scope = RoleScope.For(
            new RoleAssignment(SeamRole.PortfolioLeader, b.Id("evp1"), PortfolioId: b.Id("P1")), g);

        Assert.False(scope.AllowsIndividualRead);
        Assert.Equal(new[] { b.Id("BU1"), b.Id("BU2"), b.Id("BU3") }.OrderBy(x => x),
            scope.AggregateBusinessUnitIds.OrderBy(x => x));
    }

    // --- HIG Executive sees all BUs; Platform Admin sees none -----------------------------------

    [Fact]
    public void Hig_executive_sees_all_bus_platform_admin_sees_no_assessment_data()
    {
        var b = Org();
        var g = b.Build();

        var exec = RoleScope.For(new RoleAssignment(SeamRole.HigExecutive, b.Id("evp1")), g);
        Assert.False(exec.AllowsIndividualRead);
        Assert.Equal(4, exec.AggregateBusinessUnitIds.Count);

        var admin = RoleScope.For(new RoleAssignment(SeamRole.PlatformAdmin, b.Id("evp1")), g);
        Assert.False(admin.AllowsIndividualRead);
        Assert.Empty(admin.AggregateBusinessUnitIds);
    }

    // --- BU Lead scope is exactly their own BU with BU-wide individual reach --------------------

    [Fact]
    public void Bu_lead_scope_is_their_own_bu_with_business_unit_individual_reach()
    {
        var b = Org();
        var g = b.Build();
        var scope = RoleScope.For(new RoleAssignment(SeamRole.BuLead, b.Id("evp1"), BusinessUnitId: b.Id("BU1")), g);

        Assert.True(scope.AllowsIndividualRead);
        Assert.Equal(IndividualReadReach.BusinessUnit, scope.IndividualReadReach);
        Assert.Equal(new[] { b.Id("BU1") }, scope.AggregateBusinessUnitIds);
    }

    // --- Individual sees own team aggregate; Manager sees their own team ------------------------

    [Fact]
    public void Individual_sees_own_team_manager_sees_their_own_team()
    {
        var b = Org();
        var g = b.Build();

        var ind = RoleScope.For(new RoleAssignment(SeamRole.Individual, b.Id("ind")), g);
        Assert.False(ind.AllowsIndividualRead);
        Assert.Equal(IndividualReadReach.OwnOnly, ind.IndividualReadReach);
        Assert.Equal(new[] { b.Id("mgr") }, ind.AggregateTeamManagerIds); // team = the individual's manager

        var mgr = RoleScope.For(new RoleAssignment(SeamRole.Manager, b.Id("mgr")), g);
        Assert.True(mgr.AllowsIndividualRead);
        Assert.Equal(IndividualReadReach.DirectReports, mgr.IndividualReadReach);
        Assert.Equal(new[] { b.Id("mgr") }, mgr.AggregateTeamManagerIds);
    }

    // --- structural anchors are required for leadership scopes (no silent empty scope) ----------

    [Theory]
    [InlineData(SeamRole.BuLead)]
    [InlineData(SeamRole.GroupLeader)]
    [InlineData(SeamRole.PortfolioLeader)]
    public void Leadership_scope_requires_its_structural_anchor(SeamRole role)
    {
        var b = Org();
        var g = b.Build();
        // No anchor id supplied → must throw, never silently return an empty (fail-open) scope.
        Assert.Throws<ArgumentException>(() => RoleScope.For(new RoleAssignment(role, b.Id("evp1")), g));
    }
}
