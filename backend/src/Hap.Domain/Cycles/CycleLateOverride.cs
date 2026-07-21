namespace Hap.Domain.Cycles;

/// <summary>
/// Records who authorised a late (post-close) submission for whom (FR-002: "manager override (own
/// team only) or admin override"). Existence of a row for (<see cref="CycleId"/>, <see
/// cref="PersonId"/>) is what <see cref="Cycle.AllowsSubmission"/> is asked about — see
/// QUESTIONS.md Q-017a for why this story stops at this primitive rather than wiring an actual
/// submission endpoint (the self/manager-assessment tables belong to HAP-8, not this story's —
/// see Hap.Api.Authorization for the seam-internal type definitions, the only namespace those
/// entity names may be referenced in).
/// <see cref="GrantedByRole"/> records which authority granted it ("PlatformAdmin" or "Manager")
/// for audit-adjacent traceability, distinct from the scope check itself (performed by the
/// endpoint before this record is written).
/// </summary>
public sealed class CycleLateOverride
{
    public Guid Id { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid GrantedByPersonId { get; private set; }
    public string GrantedByRole { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public CycleLateOverride(
        Guid id,
        Guid cycleId,
        Guid personId,
        Guid grantedByPersonId,
        string grantedByRole,
        DateTime createdAt)
    {
        Id = id;
        CycleId = cycleId;
        PersonId = personId;
        GrantedByPersonId = grantedByPersonId;
        GrantedByRole = grantedByRole;
        CreatedAt = createdAt;
    }

    public static CycleLateOverride Create(Guid cycleId, Guid personId, Guid grantedByPersonId, string grantedByRole) =>
        new(Guid.NewGuid(), cycleId, personId, grantedByPersonId, grantedByRole, DateTime.UtcNow);
}
