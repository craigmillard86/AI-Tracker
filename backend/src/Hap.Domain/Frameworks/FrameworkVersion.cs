namespace Hap.Domain.Frameworks;

/// <summary>
/// One version of a <see cref="Framework"/> (spec Key Entities; FR-001/FR-054). Content
/// (<see cref="Dimension"/>/<see cref="LevelDescriptor"/> rows) is seeded once against a
/// version and never edited afterwards — change happens by creating the next version, never by
/// mutating an existing one. <see cref="Status"/> governs which version drives
/// <c>GET /api/frameworks/current</c> (the <see cref="FrameworkVersionStatus.Active"/> one);
/// <see cref="IsLocked"/> is the independent, one-way immutability flip that HAP-7's cycle-open
/// logic sets via <see cref="Lock"/> the moment a Cycle adopts this version. Once locked,
/// <see cref="EnsureMutable"/> rejects every further write — this is the domain guard callers
/// (the seeder, admin services) must invoke before touching this version or anything seeded
/// under it.
/// </summary>
public sealed class FrameworkVersion
{
    public Guid Id { get; private set; }
    public Guid FrameworkId { get; private set; }
    public int VersionNumber { get; private set; }
    public FrameworkVersionStatus Status { get; private set; }
    public string? SourceRef { get; private set; }
    public bool IsLocked { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public FrameworkVersion(
        Guid id,
        Guid frameworkId,
        int versionNumber,
        FrameworkVersionStatus status,
        string? sourceRef,
        bool isLocked,
        DateTime createdAt)
    {
        Id = id;
        FrameworkId = frameworkId;
        VersionNumber = versionNumber;
        Status = status;
        SourceRef = sourceRef;
        IsLocked = isLocked;
        CreatedAt = createdAt;
    }

    public static FrameworkVersion Create(Guid frameworkId, int versionNumber, string? sourceRef) =>
        new(Guid.NewGuid(), frameworkId, versionNumber, FrameworkVersionStatus.Draft, sourceRef,
            isLocked: false, DateTime.UtcNow);

    /// <summary>FR-054 domain guard: throws once this version is locked. Call before any write
    /// to this version's own fields or to a Dimension/LevelDescriptor seeded under it.</summary>
    public void EnsureMutable()
    {
        if (IsLocked)
        {
            throw new FrameworkVersionLockedException(Id);
        }
    }

    /// <summary>Flips the version permanently immutable (FR-054). Called by HAP-7's cycle-open
    /// logic the moment a Cycle adopts this version. Idempotent — locking an already-locked
    /// version is a no-op, never an error (a second cycle referencing the same version, or a
    /// retried open, must not fail).
    /// Carry-forward (panel round 1, advisory A6): today this is an in-memory/EF-level guard
    /// only — nothing stops a raw SQL write to a locked version's rows. A DB-layer guard (trigger
    /// or check constraint, mirroring the audit_log append-only trigger from migration #1) should
    /// land with HAP-7, when <see cref="Lock"/> gets its first real caller.</summary>
    public void Lock() => IsLocked = true;

    /// <summary>Creates a <see cref="Frameworks.Dimension"/> under this version — the sole path
    /// for adding one (FR-054, panel round-1 B2): <see cref="EnsureMutable"/> is enforced here,
    /// in the domain content-creation path itself, rather than trusted to every caller (the
    /// seeder included) to check first.</summary>
    public Dimension AddDimension(string key, string name, int displayOrder)
    {
        EnsureMutable();
        return Dimension.Create(Id, key, name, displayOrder);
    }

    /// <summary>Creates a <see cref="Frameworks.LevelDescriptor"/> under <paramref name="dimension"/>
    /// — the sole path for adding one (FR-054, panel round-1 B2), guarded the same way as
    /// <see cref="AddDimension"/>. Also refuses a dimension that does not belong to this version
    /// (a caller bug, not a locking concern, but cheap to catch here since this is already the
    /// one place both are in scope together).</summary>
    public LevelDescriptor AddLevelDescriptor(Dimension dimension, int level, string levelName, string descriptorText)
    {
        EnsureMutable();
        if (dimension.FrameworkVersionId != Id)
        {
            throw new InvalidOperationException(
                $"Dimension {dimension.Id} belongs to FrameworkVersion {dimension.FrameworkVersionId}, not {Id}.");
        }

        return LevelDescriptor.Create(dimension.Id, level, levelName, descriptorText);
    }

    /// <summary>Promotes this version to the one served by <c>GET /api/frameworks/current</c>.
    /// Idempotent if already Active. A Retired version cannot be reactivated (a new version is
    /// the correct path back into service).</summary>
    public void Activate()
    {
        EnsureMutable();
        if (Status == FrameworkVersionStatus.Retired)
        {
            throw new InvalidOperationException(
                $"FrameworkVersion {Id} is Retired and cannot be reactivated; create a new version instead.");
        }

        Status = FrameworkVersionStatus.Active;
    }

    public void Retire()
    {
        EnsureMutable();
        Status = FrameworkVersionStatus.Retired;
    }
}
