namespace Hap.Domain.Register;

/// <summary>
/// A Harris AI Dashboard reporting category (data-model.md "HarrisCategory + HarrisStageMap"; FR-027).
/// Categories are DATA, not an enum (constitution Art. II.4): the five categories are seeded from
/// <c>docs/frameworks/harris-taxonomy.v1.json</c> by
/// <see cref="Hap.Infrastructure.Register.HarrisTaxonomySeeder"/>, keyed by <see cref="Key"/>.
/// Immutable — a category record is never edited; a taxonomy change is a new seed version.
///
/// <para><see cref="GroupReported"/> is false only for "Other", driving its exclusion from the
/// group-reported Harris counts (FR-044). <see cref="CustomerDeployed"/> marks the categories whose
/// initiatives carry a customers-in-production figure (FR-031) — the register-list mockup shows a
/// numeric customer count for customer-deployed categories and "—" for internal ones.</para>
/// </summary>
public sealed class HarrisCategory
{
    public Guid Id { get; }
    public string Key { get; }
    public string Name { get; }
    public bool GroupReported { get; }
    public bool CustomerDeployed { get; }
    public DateTime CreatedAt { get; }

    public HarrisCategory(
        Guid id,
        string key,
        string name,
        bool groupReported,
        bool customerDeployed,
        DateTime createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        GroupReported = groupReported;
        CustomerDeployed = customerDeployed;
        CreatedAt = createdAt;
    }

    public static HarrisCategory Create(string key, string name, bool groupReported, bool customerDeployed) =>
        new(Guid.NewGuid(), key, name, groupReported, customerDeployed, DateTime.UtcNow);
}
