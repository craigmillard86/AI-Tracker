using Hap.Api.Authorization;
using Hap.Domain.Org;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// A tiny fluent builder for hand-crafted <see cref="OrgGraph"/> fixtures. Aliases (strings) map to
/// stable Guids for the lifetime of one builder, so a test can wire managers and BUs by name and still
/// assert on the concrete ids via <see cref="Id"/>. Keeps the seam's unit tests exhaustive and
/// database-free (the org side of the seam, per the story).
/// </summary>
internal sealed class GraphBuilder
{
    private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);
    private readonly List<OrgPerson> _people = new();
    private readonly Dictionary<Guid, Guid> _groupOfBu = new();
    private readonly Dictionary<Guid, Guid> _portfolioOfGroup = new();

    public Guid Id(string alias) =>
        _ids.TryGetValue(alias, out var g) ? g : _ids[alias] = Guid.NewGuid();

    /// <summary>Register a BU's containment: which group and portfolio it rolls up into.</summary>
    public GraphBuilder Bu(string bu, string group, string portfolio)
    {
        _groupOfBu[Id(bu)] = Id(group);
        _portfolioOfGroup[Id(group)] = Id(portfolio);
        return this;
    }

    public GraphBuilder Person(
        string alias,
        string bu,
        string? manager = null,
        EmployeeType type = EmployeeType.Employee,
        bool active = true)
    {
        _people.Add(new OrgPerson(
            Id(alias),
            manager is null ? null : Id(manager),
            Id(bu),
            type,
            active));
        return this;
    }

    /// <summary>Add a person whose manager id points at nobody in the graph (a dangling reference).</summary>
    public GraphBuilder PersonWithDanglingManager(string alias, string bu)
    {
        _people.Add(new OrgPerson(Id(alias), Guid.NewGuid(), Id(bu), EmployeeType.Employee, true));
        return this;
    }

    public OrgGraph Build() => new(_people, _groupOfBu, _portfolioOfGroup);
}
