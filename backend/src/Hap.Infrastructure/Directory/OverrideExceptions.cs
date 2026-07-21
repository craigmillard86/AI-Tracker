namespace Hap.Infrastructure.Directory;

/// <summary>The override's subject person does not exist. Maps to 404 at the API.</summary>
public sealed class PersonNotFoundException : Exception
{
    public PersonNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>The override is malformed or unsatisfiable — its value resolves to nothing, points at
/// the subject itself, or would create a management-chain cycle. Maps to 422 at the API. Thrown
/// before anything is written, so a rejected override leaves no override row and no audit row
/// (fail-closed).</summary>
public sealed class OverrideValidationException : Exception
{
    public OverrideValidationException(string message) : base(message)
    {
    }
}
