using Hap.Domain.Org;

namespace Hap.Api.Authorization;

/// <summary>One person as the seam needs them: identity, the manager edge, home BU, employee type,
/// and the active flag. Deliberately the minimal STRUCTURAL projection — the seam decides visibility
/// from org structure (FK containment + the manager chain), never from
/// <c>HierarchyRoleResolver</c>'s unratified depth-derived tier labels (QUESTIONS.md Q-014).
/// <para>On-leave people are NOT modelled here: on-leave (FR-069) leaves <see cref="IsActive"/> true,
/// so an on-leave manager remains in the chain exactly like any other active manager — no special
/// case needed at the seam.</para></summary>
public sealed record OrgPerson(
    Guid Id,
    Guid? ManagerPersonId,
    Guid BusinessUnitId,
    EmployeeType EmployeeType,
    bool IsActive);

/// <summary>
/// An in-memory structural snapshot of the org: people by id, plus the BU → Group → Portfolio
/// containment FKs. Loaded once per use (<see cref="OrgGraphLoader"/>) and walked in memory — well
/// inside the "&lt;30s/BU" budget (research D4) at this scale. Hand-built in unit tests so the seam's
/// decisions are exhaustively testable without a database.
/// </summary>
public sealed class OrgGraph
{
    private readonly Dictionary<Guid, OrgPerson> _peopleById;
    private readonly IReadOnlyDictionary<Guid, Guid> _groupOfBu;
    private readonly IReadOnlyDictionary<Guid, Guid> _portfolioOfGroup;
    private readonly HashSet<Guid> _managersWithActiveReports;

    public OrgGraph(
        IEnumerable<OrgPerson> people,
        IReadOnlyDictionary<Guid, Guid> groupOfBu,
        IReadOnlyDictionary<Guid, Guid> portfolioOfGroup)
    {
        _peopleById = people.ToDictionary(p => p.Id);
        _groupOfBu = groupOfBu;
        _portfolioOfGroup = portfolioOfGroup;

        // Precompute who has >= 1 ACTIVE direct report — the structural "is a line manager" signal used
        // to classify a reader (a manager reads direct reports) WITHOUT depth-derived tier labels (Q-014).
        _managersWithActiveReports = _peopleById.Values
            .Where(p => p.IsActive && p.ManagerPersonId is not null)
            .Select(p => p.ManagerPersonId!.Value)
            .ToHashSet();
    }

    /// <summary>True iff <paramref name="personId"/> has at least one active direct report — i.e. is a
    /// line manager. Structural (from the manager edge), never inferred from chain depth.</summary>
    public bool HasDirectReports(Guid personId) => _managersWithActiveReports.Contains(personId);

    /// <summary>The person with this id, or null if absent (inactive-and-purged, unsynced, or a
    /// dangling manager reference pointing outside the loaded population).</summary>
    public OrgPerson? Find(Guid id) => _peopleById.TryGetValue(id, out var p) ? p : null;

    public IReadOnlyCollection<OrgPerson> People => _peopleById.Values;

    public Guid? GroupOfBu(Guid businessUnitId) =>
        _groupOfBu.TryGetValue(businessUnitId, out var g) ? g : null;

    public Guid? PortfolioOfGroup(Guid groupId) =>
        _portfolioOfGroup.TryGetValue(groupId, out var p) ? p : null;

    /// <summary>Active people homed in the given BU (individual membership / BU-aggregate base).</summary>
    public IReadOnlyList<OrgPerson> ActivePeopleInBu(Guid businessUnitId) =>
        _peopleById.Values.Where(p => p.IsActive && p.BusinessUnitId == businessUnitId).ToList();

    /// <summary>The BU ids that roll up into a group.</summary>
    public IReadOnlyList<Guid> BusInGroup(Guid groupId) =>
        _groupOfBu.Where(kv => kv.Value == groupId).Select(kv => kv.Key).ToList();

    /// <summary>The BU ids that roll up into a portfolio (through its groups).</summary>
    public IReadOnlyList<Guid> BusInPortfolio(Guid portfolioId)
    {
        var groups = _portfolioOfGroup
            .Where(kv => kv.Value == portfolioId)
            .Select(kv => kv.Key)
            .ToHashSet();
        return _groupOfBu.Where(kv => groups.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    /// <summary>Every BU id known to the graph (the all-HIG aggregate base).</summary>
    public IReadOnlyList<Guid> AllBusinessUnitIds => _groupOfBu.Keys.ToList();
}
