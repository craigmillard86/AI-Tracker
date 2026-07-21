namespace Hap.Domain.Cycles;

/// <summary>
/// One person's participation record for a cycle, derived ONCE at cycle open (FR-003) from the
/// whole active-person population — never recomputed afterwards, which is exactly why a BU
/// onboarded mid-cycle gets no invitations until the NEXT open (FR-002 test). Every active person
/// gets exactly one row per cycle: either a real invitation (<see cref="Invited"/> — <see
/// cref="Excluded"/> false, <see cref="InvitedAt"/> set) or an excluded row recording why (<see
/// cref="ExcludedFor"/>) — this is what gives <see cref="InvitationExclusionReason.NotOnboarded"/>
/// somewhere to mean something, matching data-model.md's excluded_reason enum exactly
/// (QUESTIONS.md Q-016).
/// </summary>
public sealed class CycleInvitation
{
    public Guid Id { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateTime? InvitedAt { get; private set; }
    public bool Excluded { get; private set; }
    public InvitationExclusionReason? ExcludedReason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public CycleInvitation(
        Guid id,
        Guid cycleId,
        Guid personId,
        DateTime? invitedAt,
        bool excluded,
        InvitationExclusionReason? excludedReason,
        DateTime createdAt)
    {
        Id = id;
        CycleId = cycleId;
        PersonId = personId;
        InvitedAt = invitedAt;
        Excluded = excluded;
        ExcludedReason = excludedReason;
        CreatedAt = createdAt;
    }

    /// <summary>A real invitation: the person is active, in an onboarded BU, and not an excluded
    /// contractor (FR-003).</summary>
    public static CycleInvitation Invited(Guid cycleId, Guid personId) =>
        new(Guid.NewGuid(), cycleId, personId, DateTime.UtcNow, excluded: false, excludedReason: null, DateTime.UtcNow);

    /// <summary>An excluded row: no invitation email row exists for this person, but the row itself
    /// still records why (FR-005/FR-003 test: "excluded=true, reason=Contractor, no email row").</summary>
    public static CycleInvitation ExcludedFor(Guid cycleId, Guid personId, InvitationExclusionReason reason) =>
        new(Guid.NewGuid(), cycleId, personId, invitedAt: null, excluded: true, reason, DateTime.UtcNow);
}
