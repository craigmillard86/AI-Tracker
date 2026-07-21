namespace Hap.Api.Authorization;

/// <summary>
/// The seven visibility roles of the spec "Users and roles" table. Distinct from
/// <c>Hap.Domain.Org.OrgRole</c> (the explicit-GRANT enum: PlatformAdmin/HigExecutive/BuDelegate/
/// GroupViewer): this enumerates the FULL role taxonomy the "Sees" column scopes, including the
/// hierarchy tiers. <see cref="RoleScope"/> maps a role (+ the org unit it is anchored to) to the
/// nodes it may see.
/// </summary>
public enum SeamRole
{
    Individual,
    Manager,
    BuLead,
    GroupLeader,
    PortfolioLeader,
    HigExecutive,
    PlatformAdmin,
}

/// <summary>How far a role's INDIVIDUAL-score reach extends. Above-BU roles are
/// <see cref="None"/> — no individual-read capability by construction (FR-025 clause 2).</summary>
public enum IndividualReadReach
{
    /// <summary>No individual reads at all (Group Leader, Portfolio Leader, HIG Executive, Platform Admin).</summary>
    None,

    /// <summary>Only the caller's own assessment (Individual).</summary>
    OwnOnly,

    /// <summary>The caller's direct-report team (Manager) — actual reads still gated by the chain.</summary>
    DirectReports,

    /// <summary>Every individual in the caller's BU (BU Lead) — actual reads still gated by the chain.</summary>
    BusinessUnit,
}

/// <summary>
/// A role held over a specific org anchor. The anchor ids are the STRUCTURAL inputs — the person's
/// home team/BU, or the BU/group/portfolio a leader is responsible for — supplied by the
/// caller-context builder from FK membership and (re-read) <c>RoleGrant</c> rows, NEVER inferred from
/// chain depth (Q-014). <b>Re-read note (HAP-4 A3):</b> a BU-scoped grant flattens to a bare role
/// string in the auth cookie — the anchoring <c>BusinessUnitId</c> is NOT in the claim — so whoever
/// builds a <see cref="RoleAssignment"/> for a BU-scoped decision MUST read the grant's scope from the
/// database, not the cookie. RoleScope takes the anchor as an explicit input precisely so that read
/// happens at the boundary and never leaks a stale claim into a scope decision.
/// </summary>
public sealed record RoleAssignment(
    SeamRole Role,
    Guid PersonId,
    Guid? BusinessUnitId = null,
    Guid? GroupId = null,
    Guid? PortfolioId = null);

/// <summary>
/// What a role assignment may see. <see cref="AllowsIndividualRead"/> is the FR-025 clause-2 invariant
/// surface: FALSE for every above-BU role and for Platform Admin, by construction. Individual-score
/// reads are ultimately gated by <see cref="ChainResolver"/> in the gateway; this flag asserts, at the
/// role-scope layer, which roles may reach individual data AT ALL.
/// </summary>
public sealed record VisibilityScope(
    bool AllowsIndividualRead,
    IndividualReadReach IndividualReadReach,
    IReadOnlyList<Guid> AggregateTeamManagerIds,
    IReadOnlyList<Guid> AggregateBusinessUnitIds);

/// <summary>
/// Maps a <see cref="RoleAssignment"/> to the org nodes it may see, STRUCTURALLY (FK containment) —
/// the spec "Users and roles" "Sees" column made executable. Never consults
/// <c>HierarchyRoleResolver</c>'s depth-derived labels (Q-014). Group/Portfolio/Executive/Admin scopes
/// contain no individual-read capability by construction (FR-025).
/// </summary>
public static class RoleScope
{
    /// <summary>
    /// The individual-score read CAPABILITY of a role, independent of any org anchor — the FR-025
    /// clause-2 invariant expressed as a pure function of the role alone. This is the single source of
    /// truth the gateway gates on (an above-BU / admin role returns <c>false</c>), and
    /// <see cref="For"/> uses it too, so the anchored scope and the bare capability can never diverge.
    /// </summary>
    public static (bool AllowsIndividualRead, IndividualReadReach Reach) IndividualReadCapability(SeamRole role) =>
        role switch
        {
            SeamRole.Individual => (false, IndividualReadReach.OwnOnly),      // own scores only, no reads of others
            SeamRole.Manager => (true, IndividualReadReach.DirectReports),
            SeamRole.BuLead => (true, IndividualReadReach.BusinessUnit),
            SeamRole.GroupLeader => (false, IndividualReadReach.None),
            SeamRole.PortfolioLeader => (false, IndividualReadReach.None),
            SeamRole.HigExecutive => (false, IndividualReadReach.None),
            SeamRole.PlatformAdmin => (false, IndividualReadReach.None),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown seam role."),
        };

    public static VisibilityScope For(RoleAssignment assignment, OrgGraph graph)
    {
        var (allowsIndividualRead, reach) = IndividualReadCapability(assignment.Role);
        IReadOnlyList<Guid> teamManagers = Array.Empty<Guid>();
        IReadOnlyList<Guid> aggregateBus = Array.Empty<Guid>();

        switch (assignment.Role)
        {
            case SeamRole.Individual:
                // Own assessments + own team's aggregate, keyed by the caller's manager id (no Team
                // entity exists — a team is a manager and their reports).
                var managerId = graph.Find(assignment.PersonId)?.ManagerPersonId;
                teamManagers = managerId is null ? Array.Empty<Guid>() : new[] { managerId.Value };
                break;

            case SeamRole.Manager:
                teamManagers = new[] { assignment.PersonId }; // own team aggregate, keyed by the manager id
                break;

            case SeamRole.BuLead:
                RequireAnchor(assignment.BusinessUnitId, "BU Lead", nameof(assignment.BusinessUnitId));
                aggregateBus = new[] { assignment.BusinessUnitId!.Value };
                break;

            case SeamRole.GroupLeader:
                RequireAnchor(assignment.GroupId, "Group Leader", nameof(assignment.GroupId));
                aggregateBus = graph.BusInGroup(assignment.GroupId!.Value);
                break;

            case SeamRole.PortfolioLeader:
                RequireAnchor(assignment.PortfolioId, "Portfolio Leader", nameof(assignment.PortfolioId));
                aggregateBus = graph.BusInPortfolio(assignment.PortfolioId!.Value);
                break;

            case SeamRole.HigExecutive:
                aggregateBus = graph.AllBusinessUnitIds; // consolidated all-HIG aggregate
                break;

            case SeamRole.PlatformAdmin:
                break; // framework/cycle/org administration — no assessment data at all

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(assignment), assignment.Role, "Unknown seam role.");
        }

        return new VisibilityScope(allowsIndividualRead, reach, teamManagers, aggregateBus);
    }

    private static void RequireAnchor(Guid? anchor, string role, string field)
    {
        if (anchor is null)
        {
            throw new ArgumentException(
                $"{role} role scope requires a structural {field} anchor (re-read from the database, " +
                "never the cookie claim — HAP-4 A3).", field);
        }
    }
}
