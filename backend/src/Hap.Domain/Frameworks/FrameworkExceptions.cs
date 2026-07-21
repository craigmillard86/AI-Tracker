namespace Hap.Domain.Frameworks;

/// <summary>
/// Thrown by <see cref="FrameworkVersion.EnsureMutable"/> (FR-054): once a Cycle has adopted a
/// version, HAP-7's cycle-open logic calls <see cref="FrameworkVersion.Lock"/> and every
/// subsequent write to that version — or to the dimensions/descriptors seeded under it — must
/// be rejected. This is the domain guard every write path is required to call before mutating
/// version-scoped content; it is not enforced by trusting the caller to check
/// <see cref="FrameworkVersion.IsLocked"/> itself.
/// </summary>
public sealed class FrameworkVersionLockedException : Exception
{
    public Guid FrameworkVersionId { get; }

    public FrameworkVersionLockedException(Guid frameworkVersionId)
        : base($"FrameworkVersion {frameworkVersionId} is locked (in use by a cycle) and cannot be modified.")
    {
        FrameworkVersionId = frameworkVersionId;
    }
}
