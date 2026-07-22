namespace Hap.Domain.Rollups;

/// <summary>
/// The kind of org node a <see cref="RollupSnapshot"/> aggregates (data-model.md RollupSnapshot
/// <c>org_node_type</c>). The fixed rollup hierarchy, leaf-to-root: a <see cref="Team"/> is a manager
/// and their reports; teams roll into a <see cref="Bu"/>, BUs into a <see cref="Group"/>, groups into a
/// <see cref="Portfolio"/>, and portfolios into the single <see cref="AllHig"/> node. This is the only
/// publication surface in v1 (research D2) — the complement-suppression tree is exactly these levels.
/// </summary>
public enum OrgNodeType
{
    Team,
    Bu,
    Group,
    Portfolio,
    AllHig,
}
