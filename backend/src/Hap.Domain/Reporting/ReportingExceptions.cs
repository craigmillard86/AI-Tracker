namespace Hap.Domain.Reporting;

/// <summary>A BU AI-DLC declaration or monthly-metrics submission failed a domain invariant on
/// Create/Update — a missing business unit or declaring/submitting person, a declared level outside
/// 0–3, or a negative/out-of-range metric figure (FR-047/FR-048). Maps to 422 at the API (mirrors
/// <c>Hap.Domain.Register.InitiativeValidationException</c>).</summary>
public sealed class BuReportingValidationException : Exception
{
    public BuReportingValidationException(string message) : base(message)
    {
    }
}
