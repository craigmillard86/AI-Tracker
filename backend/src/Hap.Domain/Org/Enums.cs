namespace Hap.Domain.Org;

/// <summary>Directory-sourced employment classification (FR-006). Contractors are
/// excluded from assessment participation but may still be line managers.</summary>
public enum EmployeeType
{
    Employee,
    Contractor,
}

/// <summary>The relationship an <see cref="OrgOverride"/> corrects (FR-023). Structural
/// fields (<see cref="BusinessUnit"/>, <see cref="Manager"/>) are re-applied on top of
/// each directory import so a correction is never clobbered by re-sync. <see cref="DottedLine"/>
/// is recorded and audited but advisory only in v1 (no management-chain effect).</summary>
public enum OverrideField
{
    BusinessUnit,
    Manager,
    DottedLine,
}

/// <summary>Explicit, granted roles (FR-056). Hierarchy-derived roles (Manager, BU Lead,
/// Group Leader, Portfolio Leader) are computed from org structure and are never stored.</summary>
public enum OrgRole
{
    PlatformAdmin,
    HigExecutive,
    BuDelegate,
    GroupViewer,
}
