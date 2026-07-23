namespace Hap.Domain.Register;

/// <summary>An initiative failed a domain invariant on <see cref="Initiative.Create"/> or an edit —
/// a missing required field (name, BU, category, owner), or an AI-DLC level outside 1–3 (FR-027).
/// Maps to 422 at the API.</summary>
public sealed class InitiativeValidationException : Exception
{
    public InitiativeValidationException(string message) : base(message)
    {
    }
}

/// <summary>An initiative stage transition was attempted backward, as a same-stage no-op, or from the
/// terminal <see cref="InitiativeStage.Retired"/> stage (FR-028 — forward-only; Retired is terminal).
/// Maps to 409 at the API (contracts/api.md "Register").</summary>
public sealed class InitiativeStageTransitionException : Exception
{
    public InitiativeStageTransitionException(string message) : base(message)
    {
    }
}
