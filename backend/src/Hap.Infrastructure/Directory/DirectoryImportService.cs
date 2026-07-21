using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Directory;

/// <summary>Counts returned by a sync run, for the endpoint response and test assertions.</summary>
public sealed record DirectoryImportResult(
    int Portfolios,
    int Groups,
    int BusinessUnits,
    int People,
    int Deactivated,
    int OverridesReapplied);

/// <summary>
/// Imports a directory snapshot into the org model (FR-020/021/024). The import is:
/// <list type="bullet">
/// <item><b>Idempotent</b> — everything upserts by natural key (person <c>external_ref</c>,
/// BU <c>code</c>, group/portfolio name); re-running the same snapshot changes nothing.</item>
/// <item><b>Non-destructive</b> — a person who leaves the snapshot, or is flagged inactive in
/// it, is deactivated (<c>is_active=false</c>) and retained. Nothing is ever deleted (FR-024).</item>
/// <item><b>Override-preserving</b> — <c>OrgOverride</c> rows are never touched; after the
/// snapshot is applied, structural overrides are re-applied so a correction out-lives re-sync (FR-023).</item>
/// </list>
/// The whole run is one transaction: a partial import cannot be observed.
/// </summary>
public sealed class DirectoryImportService
{
    private readonly HapDbContext _db;
    private readonly IDirectorySource _source;

    public DirectoryImportService(HapDbContext db, IDirectorySource source)
    {
        _db = db;
        _source = source;
    }

    public async Task<DirectoryImportResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _source.FetchSnapshotAsync(cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var portfolioByName = await _db.Portfolios.ToDictionaryAsync(p => p.Name, cancellationToken);
        var groupByKey = (await _db.Groups.ToListAsync(cancellationToken))
            .ToDictionary(g => GroupKey(g.PortfolioId, g.Name));
        var buByCode = await _db.BusinessUnits.ToDictionaryAsync(b => b.Code, cancellationToken);
        var people = await _db.People.ToListAsync(cancellationToken);
        var personByRef = people.ToDictionary(p => p.ExternalRef, StringComparer.Ordinal);

        // --- Portfolios / groups / BUs -------------------------------------------------
        foreach (var bu in snapshot.Bus)
        {
            if (!portfolioByName.TryGetValue(bu.Portfolio, out var portfolio))
            {
                portfolio = Portfolio.Create(bu.Portfolio);
                _db.Portfolios.Add(portfolio);
                portfolioByName[bu.Portfolio] = portfolio;
            }

            var groupKey = GroupKey(portfolio.Id, bu.Group);
            if (!groupByKey.TryGetValue(groupKey, out var group))
            {
                group = GroupOrg.Create(bu.Group, portfolio.Id);
                _db.Groups.Add(group);
                groupByKey[groupKey] = group;
            }

            if (!buByCode.TryGetValue(bu.Code, out var businessUnit))
            {
                businessUnit = BusinessUnit.Create(bu.Code, bu.Name, group.Id, directorySource: "Synthetic");
                _db.BusinessUnits.Add(businessUnit);
                buByCode[bu.Code] = businessUnit;
            }
            else
            {
                businessUnit.ApplyDirectory(bu.Name, group.Id, "Synthetic");
            }
        }

        // --- People, pass 1: scalar attributes + BU (manager resolved in pass 2) --------
        var seenRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dp in snapshot.Persons)
        {
            seenRefs.Add(dp.ExternalRef);
            var employeeType = ParseEmployeeType(dp.EmployeeType);

            if (!buByCode.TryGetValue(dp.BuCode, out var bu))
            {
                throw new InvalidOperationException(
                    $"Snapshot person '{dp.ExternalRef}' references unknown BU code '{dp.BuCode}'.");
            }

            if (!personByRef.TryGetValue(dp.ExternalRef, out var person))
            {
                person = new Person(
                    Guid.NewGuid(), dp.ExternalRef, dp.Name, dp.Email, dp.JobTitle,
                    managerPersonId: null, bu.Id, employeeType, dp.IsActive, dp.OnLeave, DateTime.UtcNow);
                _db.People.Add(person);
                personByRef[dp.ExternalRef] = person;
            }
            else
            {
                person.ApplyDirectoryAttributes(dp.Name, dp.Email, dp.JobTitle, employeeType, dp.IsActive, dp.OnLeave);
                person.SetBusinessUnit(bu.Id);
            }
        }

        // --- Leavers: in the store but absent from this snapshot => deactivate, retain ---
        int deactivated = 0;
        foreach (var person in personByRef.Values)
        {
            if (!seenRefs.Contains(person.ExternalRef) && person.IsActive)
            {
                person.Deactivate();
                deactivated++;
            }
        }

        // --- People, pass 2: manager links (ids now exist for everyone) ------------------
        // A non-null manager reference that does not resolve, or points at the person itself, is
        // a corrupt snapshot — fail the whole import rather than silently null it (consistent with
        // the unknown-BU guard above). A null reference is legitimate (org root, directory gap).
        foreach (var dp in snapshot.Persons)
        {
            var person = personByRef[dp.ExternalRef];
            Guid? managerId = null;
            if (dp.ManagerExternalRef is not null)
            {
                if (string.Equals(dp.ManagerExternalRef, dp.ExternalRef, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Snapshot person '{dp.ExternalRef}' lists itself as its own manager.");
                }

                if (!personByRef.TryGetValue(dp.ManagerExternalRef, out var manager))
                {
                    throw new InvalidOperationException(
                        $"Snapshot person '{dp.ExternalRef}' references unknown manager '{dp.ManagerExternalRef}'.");
                }

                managerId = manager.Id;
            }
            person.SetManager(managerId);
        }

        // --- Re-apply overrides on top of the imported data (FR-023) --------------------
        var personById = personByRef.Values.ToDictionary(p => p.Id);
        var overrides = await _db.OrgOverrides.ToListAsync(cancellationToken);
        int overridesReapplied = 0;
        foreach (var ov in overrides.OrderBy(o => o.CreatedAt))
        {
            if (!personById.TryGetValue(ov.PersonId, out var person))
            {
                continue;
            }

            if (ApplyOverride(ov, person, buByCode, personByRef))
            {
                overridesReapplied++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new DirectoryImportResult(
            Portfolios: portfolioByName.Count,
            Groups: groupByKey.Count,
            BusinessUnits: buByCode.Count,
            People: personByRef.Count,
            Deactivated: deactivated,
            OverridesReapplied: overridesReapplied);
    }

    /// <summary>Applies one override's structural effect to a person. Returns true if it changed
    /// the primary chain (BU or manager). <see cref="OverrideField.DottedLine"/> is advisory in v1.</summary>
    internal static bool ApplyOverride(
        OrgOverride ov,
        Person person,
        IReadOnlyDictionary<string, BusinessUnit> buByCode,
        IReadOnlyDictionary<string, Person> personByRef)
    {
        switch (ov.Field)
        {
            case OverrideField.BusinessUnit:
                if (buByCode.TryGetValue(ov.OverrideValue, out var bu))
                {
                    person.SetBusinessUnit(bu.Id);
                    return true;
                }
                return false;

            case OverrideField.Manager:
                if (personByRef.TryGetValue(ov.OverrideValue, out var manager))
                {
                    person.SetManager(manager.Id);
                    return true;
                }
                return false;

            case OverrideField.DottedLine:
            default:
                return false;
        }
    }

    private static string GroupKey(Guid portfolioId, string name) => $"{portfolioId:N}{name}";

    private static EmployeeType ParseEmployeeType(string value) =>
        string.Equals(value, "Contractor", StringComparison.OrdinalIgnoreCase)
            ? EmployeeType.Contractor
            : EmployeeType.Employee;
}
