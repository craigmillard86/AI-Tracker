namespace Hap.Domain.Frameworks;

/// <summary>
/// One scored dimension of a <see cref="FrameworkVersion"/> (spec Key Entities; FR-001) — one
/// axis the assessment scores a person against (e.g. how AI is used day to day, or what unit of
/// work looks like). <see cref="DisplayOrder"/> is the position from the source JSON's
/// <c>dimensions</c> array, preserved so the assessment UI and <c>GET /api/frameworks/current</c>
/// render dimensions in the framework's authored order. Immutable once created (get-only): a
/// dimension is seeded exactly once per version by
/// <see cref="Hap.Infrastructure.Frameworks.FrameworkSeeder"/>, which must call the owning
/// <see cref="FrameworkVersion.EnsureMutable"/> before creating one (FR-054) — there is no
/// in-place edit, only a new version.
/// </summary>
public sealed class Dimension
{
    public Guid Id { get; }
    public Guid FrameworkVersionId { get; }
    public string Key { get; }
    public string Name { get; }
    public int DisplayOrder { get; }
    public DateTime CreatedAt { get; }

    public Dimension(
        Guid id,
        Guid frameworkVersionId,
        string key,
        string name,
        int displayOrder,
        DateTime createdAt)
    {
        Id = id;
        FrameworkVersionId = frameworkVersionId;
        Key = key;
        Name = name;
        DisplayOrder = displayOrder;
        CreatedAt = createdAt;
    }

    public static Dimension Create(Guid frameworkVersionId, string key, string name, int displayOrder) =>
        new(Guid.NewGuid(), frameworkVersionId, key, name, displayOrder, DateTime.UtcNow);
}
