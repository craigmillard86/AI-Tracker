namespace Hap.Domain.Cycles;

/// <summary>Forward-only violation (FR-002/FR-060): any transition other than Draft→Open→Closed —
/// Draft→Closed, Closed→Open, a repeat of the current state, etc. Maps to 409 at the API.</summary>
public sealed class CycleStateTransitionException : Exception
{
    public Guid CycleId { get; }
    public CycleState FromState { get; }
    public CycleState AttemptedState { get; }

    public CycleStateTransitionException(Guid cycleId, CycleState fromState, CycleState attemptedState)
        : base($"Cycle {cycleId} cannot transition {fromState} -> {attemptedState}; " +
               "only Draft -> Open -> Closed is permitted.")
    {
        CycleId = cycleId;
        FromState = fromState;
        AttemptedState = attemptedState;
    }
}

/// <summary>FR-002 "one Open cycle per framework": raised when opening a cycle whose framework
/// already has another Open cycle. Maps to 409 at the API.</summary>
public sealed class DuplicateOpenCycleException : Exception
{
    public Guid FrameworkId { get; }

    public DuplicateOpenCycleException(Guid frameworkId)
        : base($"Framework {frameworkId} already has an Open cycle; only one Open cycle per framework is permitted.")
    {
        FrameworkId = frameworkId;
    }
}

/// <summary>The referenced cycle does not exist. Maps to 404 at the API.</summary>
public sealed class CycleNotFoundException : Exception
{
    public Guid CycleId { get; }

    public CycleNotFoundException(Guid cycleId) : base($"Cycle {cycleId} does not exist.") =>
        CycleId = cycleId;
}
