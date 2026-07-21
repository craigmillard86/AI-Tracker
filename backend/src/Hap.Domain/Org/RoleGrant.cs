namespace Hap.Domain.Org;

/// <summary>
/// An explicit role grant (FR-056). Append-only, like the audit log it is paired with: a
/// grant is a historical fact, so the type is immutable and there is no revoke-by-mutation
/// path. (Revocation, when a story needs it, is a new superseding record — not an edit.)
/// <see cref="BusinessUnitId"/> scopes a <see cref="OrgRole.BuDelegate"/> to one BU (FR-047).
/// Every grant also writes an <c>AuditLog</c> row (FR-050). The grant endpoint itself lands
/// with the identity/roles story; this entity is part of migration #1's foundation.
/// </summary>
public sealed class RoleGrant
{
    public Guid Id { get; }
    public Guid PersonId { get; }
    public OrgRole Role { get; }
    public Guid? BusinessUnitId { get; }
    public string GrantedBy { get; }
    public DateTime CreatedAt { get; }

    public RoleGrant(
        Guid id,
        Guid personId,
        OrgRole role,
        Guid? businessUnitId,
        string grantedBy,
        DateTime createdAt)
    {
        Id = id;
        PersonId = personId;
        Role = role;
        BusinessUnitId = businessUnitId;
        GrantedBy = grantedBy;
        CreatedAt = createdAt;
    }

    public static RoleGrant Create(Guid personId, OrgRole role, Guid? businessUnitId, string grantedBy) =>
        new(Guid.NewGuid(), personId, role, businessUnitId, grantedBy, DateTime.UtcNow);
}
