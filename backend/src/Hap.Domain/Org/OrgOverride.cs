namespace Hap.Domain.Org;

/// <summary>
/// A manual correction to a directory-synced relationship (FR-023). Immutable once written:
/// a correction is a historical fact. Override rows are never touched by directory sync; the
/// structural fields (<see cref="OverrideField.BusinessUnit"/>, <see cref="OverrideField.Manager"/>)
/// are re-applied on top of every import so a correction survives and re-asserts itself.
/// Every write of one of these also writes exactly one <c>AuditLog</c> row (enforced by the
/// service, tested under Category=PrivacyReporting).
/// </summary>
public sealed class OrgOverride
{
    public Guid Id { get; }
    public Guid PersonId { get; }
    public OverrideField Field { get; }

    /// <summary>The value in effect before the correction (for the audit trail); may be null.</summary>
    public string? OriginalValue { get; }

    /// <summary>The corrected value. For <see cref="OverrideField.BusinessUnit"/> a BU code;
    /// for <see cref="OverrideField.Manager"/>/<see cref="OverrideField.DottedLine"/> a person external_ref.</summary>
    public string OverrideValue { get; }

    public string Reason { get; }
    public string CreatedBy { get; }
    public DateTime CreatedAt { get; }

    public OrgOverride(
        Guid id,
        Guid personId,
        OverrideField field,
        string? originalValue,
        string overrideValue,
        string reason,
        string createdBy,
        DateTime createdAt)
    {
        Id = id;
        PersonId = personId;
        Field = field;
        OriginalValue = originalValue;
        OverrideValue = overrideValue;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static OrgOverride Create(
        Guid personId,
        OverrideField field,
        string? originalValue,
        string overrideValue,
        string reason,
        string createdBy) =>
        new(Guid.NewGuid(), personId, field, originalValue, overrideValue, reason, createdBy, DateTime.UtcNow);
}
