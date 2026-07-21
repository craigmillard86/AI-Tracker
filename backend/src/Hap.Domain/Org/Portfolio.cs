namespace Hap.Domain.Org;

/// <summary>A reporting portfolio — a collection of groups (spec Key Entities).</summary>
public sealed class Portfolio
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Portfolio(Guid id, string name, DateTime createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public static Portfolio Create(string name) => new(Guid.NewGuid(), name, DateTime.UtcNow);
}
