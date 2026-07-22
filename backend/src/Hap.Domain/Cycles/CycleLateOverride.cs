namespace Hap.Domain.Cycles;

/// <summary>
/// Records who authorised a late (post-close) submission for whom (FR-002: "manager override (own
/// team only) or admin override"). Existence of a row for (<see cref="CycleId"/>, <see
/// cref="PersonId"/>) is what <see cref="Cycle.AllowsSubmission"/> is asked about — see
/// QUESTIONS.md Q-017a for why HAP-7 stopped at this primitive rather than wiring an actual
/// submission endpoint. HAP-8 wired it: the self-assessment submit + score-write paths consult
/// <see cref="Cycle.AllowsSubmission"/> with this row's existence. The assessment entities now live in
/// the domain's assessments namespace, and their tables are queried only through the visibility seam
/// (<c>Hap.Api.Authorization</c>) — architecture-guarded.
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
