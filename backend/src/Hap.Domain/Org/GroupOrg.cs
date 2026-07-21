namespace Hap.Domain.Org;

/// <summary>A group — a collection of business units within a portfolio (spec Key Entities).
/// Named <c>GroupOrg</c> to avoid collision with the ubiquitous LINQ "group".</summary>
public sealed class GroupOrg
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Guid PortfolioId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public GroupOrg(Guid id, string name, Guid portfolioId, DateTime createdAt)
    {
        Id = id;
        Name = name;
        PortfolioId = portfolioId;
        CreatedAt = createdAt;
    }

    public static GroupOrg Create(string name, Guid portfolioId) =>
        new(Guid.NewGuid(), name, portfolioId, DateTime.UtcNow);

    /// <summary>Re-home the group under a different portfolio (directory correction on re-sync).</summary>
    public void SetPortfolio(Guid portfolioId) => PortfolioId = portfolioId;
}
