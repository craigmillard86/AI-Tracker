namespace Hap.Domain.Cycles;

/// <summary>Why a <see cref="CycleInvitation"/> row was excluded from the cycle (data-model.md
/// "CycleInvitation"; FR-003/FR-005). <see cref="NotOnboarded"/> exists because invitation
/// generation writes one row per active person, not just those in onboarded BUs — see
/// <see cref="CycleInvitation"/>'s class doc and QUESTIONS.md Q-016.</summary>
public enum InvitationExclusionReason
{
    Contractor,
    NotOnboarded,
}
