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
