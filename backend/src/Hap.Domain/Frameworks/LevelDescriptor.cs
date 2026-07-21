namespace Hap.Domain.Frameworks;

/// <summary>
/// The 0–3 descriptor text for one <see cref="Dimension"/> at one maturity level (spec Key
/// Entities; FR-001) — the human-readable sentence describing what that level looks like in
/// practice for this dimension. <see cref="LevelName"/> is the framework-wide label for
/// <see cref="Level"/> (e.g. the top autonomy level's name), denormalised onto every descriptor
/// row rather than modelled as a separate entity — data-model.md defines no standalone
/// level-name table, and the value only ever repeats across the (typically 7) dimensions of one
/// version. Immutable once created: seeded exactly once per dimension by
/// <see cref="Hap.Infrastructure.Frameworks.FrameworkSeeder"/>, which must call the owning
/// <see cref="FrameworkVersion.EnsureMutable"/> first (FR-054).
/// </summary>
public sealed class LevelDescriptor
{
    public Guid Id { get; }
    public Guid DimensionId { get; }

    /// <summary>0–3 maturity level.</summary>
    public int Level { get; }

    public string LevelName { get; }
    public string DescriptorText { get; }
    public DateTime CreatedAt { get; }

    public LevelDescriptor(
        Guid id,
        Guid dimensionId,
        int level,
        string levelName,
        string descriptorText,
        DateTime createdAt)
    {
        Id = id;
        DimensionId = dimensionId;
        Level = level;
        LevelName = levelName;
        DescriptorText = descriptorText;
        CreatedAt = createdAt;
    }

    public static LevelDescriptor Create(Guid dimensionId, int level, string levelName, string descriptorText) =>
        new(Guid.NewGuid(), dimensionId, level, levelName, descriptorText, DateTime.UtcNow);
}
