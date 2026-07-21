namespace Hap.Domain.Cycles;

/// <summary>Lifecycle of a <see cref="Cycle"/> (data-model.md "Cycle"; FR-002/FR-060).
/// Forward-only: Draft → Open → Closed — no other transition is ever permitted, enforced by
/// <see cref="Cycle.Open"/>/<see cref="Cycle.Close"/> themselves.</summary>
public enum CycleState
{
    Draft,
    Open,
    Closed,
}
