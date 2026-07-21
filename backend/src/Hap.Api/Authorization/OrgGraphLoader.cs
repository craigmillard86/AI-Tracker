using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// Loads the structural <see cref="OrgGraph"/> from the database. Reads only the columns the seam
/// needs — id, manager edge, home BU, employee type, active flag — plus the BU → Group → Portfolio
/// containment FKs. No assessment data is touched, and NO hierarchy-tier derivation happens here
/// (Q-014): the graph is pure structure, and the seam decides visibility from it. Registered scoped
/// alongside the seam services.
/// </summary>
public sealed class OrgGraphLoader
{
    private readonly HapDbContext _db;

    public OrgGraphLoader(HapDbContext db) => _db = db;

    public async Task<OrgGraph> LoadAsync(CancellationToken cancellationToken = default)
    {
        var people = await _db.People
            .Select(p => new OrgPerson(p.Id, p.ManagerPersonId, p.BusinessUnitId, p.EmployeeType, p.IsActive))
            .ToListAsync(cancellationToken);

        var groupOfBu = await _db.BusinessUnits
            .Select(b => new { b.Id, b.GroupId })
            .ToDictionaryAsync(x => x.Id, x => x.GroupId, cancellationToken);

        var portfolioOfGroup = await _db.Groups
            .Select(g => new { g.Id, g.PortfolioId })
            .ToDictionaryAsync(x => x.Id, x => x.PortfolioId, cancellationToken);

        return new OrgGraph(people, groupOfBu, portfolioOfGroup);
    }
}
