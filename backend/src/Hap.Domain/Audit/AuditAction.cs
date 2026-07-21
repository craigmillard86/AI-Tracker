namespace Hap.Domain.Audit;

/// <summary>The audited actions (FR-050). The enum is closed: every write to the audit
/// log is one of these. Directory sync is deliberately NOT an audited action — it writes
/// no personal-data view and produces no per-person audit noise; only the correction layer
/// (<see cref="AuditAction.OrgOverride"/>) and later assessment/reporting paths do.</summary>
public enum AuditAction
{
    IndividualView,
    ScoreChange,
    RoleGrant,
    OrgOverride,
    Export,
    RetentionErasure,
}
