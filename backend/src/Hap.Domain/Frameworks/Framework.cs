namespace Hap.Domain.Frameworks;

/// <summary>
/// A named AI-maturity framework (spec Key Entities; FR-001). Framework content — the
/// dimension names, level names, and descriptor text held by its versions — is data, never
/// code (constitution Art. II.4): the only framework in the local build, "AI Maturity in the
/// SDLC", is seeded from <c>docs/frameworks/ai-maturity-sdlc.v1.json</c> by
/// <see cref="Hap.Infrastructure.Frameworks.FrameworkSeeder"/>. Immutable once created — a
/// framework record itself is never edited; content changes happen through a new
/// <see cref="FrameworkVersion"/> (FR-054).
/// </summary>
public sealed class Framework
{
    public Guid Id { get; }
    public string Key { get; }
    public string Name { get; }
    public string? Description { get; }
    public string? Owner { get; }
    public DateTime CreatedAt { get; }

    public Framework(
        Guid id,
        string key,
        string name,
        string? description,
        string? owner,
        DateTime createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        Description = description;
        Owner = owner;
        CreatedAt = createdAt;
    }

    public static Framework Create(string key, string name, string? description, string? owner) =>
        new(Guid.NewGuid(), key, name, description, owner, DateTime.UtcNow);
}
