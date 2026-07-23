namespace Hap.Domain.Register;

/// <summary>
/// One configuration row mapping an internal <see cref="InitiativeStage"/> to its Harris reporting
/// <see cref="HarrisStage"/> (data-model.md "HarrisCategory + HarrisStageMap"; FR-064). Configuration
/// data, never code (constitution Art. II.4): seeded from <c>docs/frameworks/harris-taxonomy.v1.json</c>
/// by <see cref="Hap.Infrastructure.Register.HarrisTaxonomySeeder"/>, one row per internal stage.
/// Immutable — the map is replaced by re-seeding a new taxonomy version, never edited in place.
/// </summary>
public sealed class HarrisStageMap
{
    public Guid Id { get; }
    public InitiativeStage InternalStage { get; }
    public HarrisStage HarrisStage { get; }
    public DateTime CreatedAt { get; }

    public HarrisStageMap(
        Guid id,
        InitiativeStage internalStage,
        HarrisStage harrisStage,
        DateTime createdAt)
    {
        Id = id;
        InternalStage = internalStage;
        HarrisStage = harrisStage;
        CreatedAt = createdAt;
    }

    public static HarrisStageMap Create(InitiativeStage internalStage, HarrisStage harrisStage) =>
        new(Guid.NewGuid(), internalStage, harrisStage, DateTime.UtcNow);
}
