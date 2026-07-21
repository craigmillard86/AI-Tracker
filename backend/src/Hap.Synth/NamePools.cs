namespace Hap.Synth;

/// <summary>
/// Static, self-contained pools for synthetic names, BU/group/portfolio labels,
/// and job titles. No external data, no faker library (research D8) — names are
/// drawn deterministically from these fixed lists via <see cref="DeterministicRandom"/>.
/// Duplicate person names are fine; uniqueness is carried by external_ref/email.
/// </summary>
internal static class NamePools
{
    public static readonly IReadOnlyList<string> FirstNames = new[]
    {
        "Amelia", "Noah", "Priya", "Liam", "Sofia", "Ethan", "Aisha", "Mason",
        "Chloe", "Lucas", "Freya", "Oliver", "Zara", "Harry", "Nadia", "Jack",
        "Leila", "Charlie", "Yuki", "George", "Anaya", "Oscar", "Mei", "Arthur",
        "Ines", "Henry", "Fatima", "Leo", "Grace", "Adam", "Hannah", "Samuel",
        "Rosa", "Daniel", "Elena", "Isaac", "Maya", "Thomas", "Nina", "Joseph",
    };

    public static readonly IReadOnlyList<string> LastNames = new[]
    {
        "Okafor", "Nguyen", "Patel", "Smith", "Rossi", "Kowalski", "Ahmed", "Jones",
        "Silva", "Muller", "Chen", "Brown", "Haddad", "Wilson", "Kim", "Taylor",
        "Ferreira", "Novak", "Sato", "Evans", "Kaur", "Andersson", "Wang", "Murphy",
        "Costa", "Larsen", "Ali", "Roberts", "Ivanov", "Walsh", "Dubois", "Clarke",
        "Mbeki", "Fischer", "Reyes", "Hughes", "Yilmaz", "Moreau", "Park", "Doyle",
    };

    public static readonly IReadOnlyList<string> MemberTitles = new[]
    {
        "Software Engineer", "Senior Software Engineer", "QA Engineer",
        "Business Analyst", "Product Analyst", "Data Engineer",
        "DevOps Engineer", "Support Specialist", "UX Designer",
        "Solutions Consultant", "Implementation Specialist", "Technical Writer",
    };

    public static readonly IReadOnlyList<string> BusinessUnitNames = new[]
    {
        "Clearwater Health", "Meridian Public Safety", "Cascade Utilities",
        "Northgate Education", "Silverline Financials", "Redwood Government",
        "Harbourpoint Logistics", "Beacon Insurance", "Summit Housing",
        "Ironbridge Manufacturing", "Willowbrook Care", "Kestrel Transit",
        "Amberline Retail", "Fenwick Legal", "Greystone Energy",
        "Larkspur Media", "Copperfield Telecom", "Marlow Agriculture",
        "Thornbury Pensions", "Ravenscourt Analytics", "Oakmoor Nonprofit",
        "Brightwater Tourism", "Halewood Maritime",
    };

    public static readonly IReadOnlyList<string> GroupNames = new[]
    {
        "Group Alpha", "Group Bravo", "Group Charlie",
        "Group Delta", "Group Echo", "Group Foxtrot",
    };

    public static readonly IReadOnlyList<string> PortfolioNames = new[]
    {
        "Portfolio North", "Portfolio Central", "Portfolio South",
    };
}
