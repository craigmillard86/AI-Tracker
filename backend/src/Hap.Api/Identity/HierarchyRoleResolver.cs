using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Identity;

/// <summary>The hierarchy-derived roles for one person, computed fresh per request (never stored;
/// FR-056). Each populated id names the specific org unit led, so callers can scope visibility
/// without a second lookup.</summary>
public sealed record HierarchyRoles(
    bool IsManager,
    Guid? PortfolioLeaderOfPortfolioId,
    Guid? GroupLeaderOfGroupId,
    Guid? BuLeadOfBusinessUnitId)
{
    public static readonly HierarchyRoles None = new(false, null, null, null);

    /// <summary>Human-readable role labels, matching the wording used throughout the spec/contract
    /// ("Manager", "BU Lead", "Group Leader", "Portfolio Leader"). A person can hold more than one
    /// simultaneously (a BU Lead is also a Manager).</summary>
    public IReadOnlyList<string> ToRoleNames()
    {
        var names = new List<string>();
        if (IsManager)
        {
            names.Add("Manager");
        }
        if (BuLeadOfBusinessUnitId is not null)
        {
            names.Add("BU Lead");
        }
        if (GroupLeaderOfGroupId is not null)
        {
            names.Add("Group Leader");
        }
        if (PortfolioLeaderOfPortfolioId is not null)
        {
            names.Add("Portfolio Leader");
        }
        return names;
    }
}

/// <summary>
/// Computes hierarchy-derived roles per request (FR-056; data-model.md RoleGrant note: "Hierarchy-
/// derived roles ... are computed from org structure, never stored"). See QUESTIONS.md Q-014 for the
/// full derivation rationale and why simpler heuristics (home-BU coincidence, manager-subtree span)
/// misclassify against this generator's own engineered edge cases.
///
/// Rule: classify by manager-chain depth from a validated root — active, no manager, and >= 1 active
/// direct report (the "has reports" clause is what excludes a null-manager directory-gap fixture from
/// being mistaken for a root). Depth 1 = Portfolio Leader, depth 2 = Group Leader, depth 3 = BU Lead,
/// each of whichever BU/Group/Portfolio the person is themselves homed in (via the existing,
/// non-inferred <c>BusinessUnit.GroupId</c> / <c>GroupOrg.PortfolioId</c> FKs — no inference needed
/// there) — PROVIDED the person also has >= 1 active direct report themselves (L3 code-review
/// finding: a depth-1/2/3 person with zero active reports — e.g. a since-vacated leadership slot —
/// must not carry a leadership label; see <c>HierarchyRoleResolverTests</c>). "Manager" = has >= 1
/// active direct report, independent of depth, so a BU Lead is also a Manager (matches
/// contracts/api.md's "Manager scope"). HIG Executive (depth 0) is deliberately NOT hierarchy-derived
/// here — per RoleGrant's own doc comment it is an explicit-grant type (QUESTIONS.md Q-013 covers how
/// the dev fixture gets it).
///
/// KNOWN LIMITATION (QUESTIONS.md Q-014, elevated to an owner-ratification item after hap-red-team's
/// L3 finding): this rule assumes a UNIFORM-DEPTH org tree — every leadership tier sits at exactly
/// one depth from a single root. An interim/dual-hat layer, or a missing tier, breaks it (e.g.
/// Exec->PortfolioLeader->GroupLeader->InterimCover->RealBuLead mislabels the interim cover "BU Lead"
/// at depth 3 and demotes the real BU Lead to "Manager" only at depth 4). Fixing this needs a
/// structural "this person leads this unit" anchor — an owner decision and a migration, not
/// available in this story. Callers MUST NOT use these labels for visibility scope until Q-014 is
/// ratified or independently cleared for their use case.
///
/// The whole active population (id, manager id, BU id only) is loaded once per call and walked in
/// memory — well inside "&lt;30s/BU trivially met" (research D4) at this scale.
/// </summary>
public sealed class HierarchyRoleResolver
{
    private readonly HapDbContext _db;

    public HierarchyRoleResolver(HapDbContext db) => _db = db;

    private sealed record PersonNode(Guid Id, Guid? ManagerPersonId, Guid BusinessUnitId);

    public async Task<HierarchyRoles> ResolveAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var people = await _db.People
            .Where(p => p.IsActive)
            .Select(p => new PersonNode(p.Id, p.ManagerPersonId, p.BusinessUnitId))
            .ToListAsync(cancellationToken);

        var byId = people.ToDictionary(p => p.Id);
        if (!byId.TryGetValue(personId, out var self))
        {
            // Inactive, or not yet synced — no hierarchy role beyond the baseline (Individual).
            return HierarchyRoles.None;
        }

        var activeReportCounts = people
            .Where(p => p.ManagerPersonId.HasValue)
            .GroupBy(p => p.ManagerPersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        bool isManager = activeReportCounts.ContainsKey(personId);

        var roots = people
            .Where(p => p.ManagerPersonId is null && activeReportCounts.ContainsKey(p.Id))
            .Select(p => p.Id)
            .ToHashSet();

        int? depth = ComputeDepthFromRoot(personId, byId, roots);

        Guid? portfolioLeaderOf = null;
        Guid? groupLeaderOf = null;
        Guid? buLeadOf = null;

        // Gate on isManager too: a depth-1/2/3 person with zero active reports (e.g. a vacated
        // leadership slot still sitting at that graph position) must not carry a leadership label.
        if (isManager && depth is 1 or 2 or 3)
        {
            var bu = await _db.BusinessUnits
                .Where(b => b.Id == self.BusinessUnitId)
                .Select(b => new { b.GroupId })
                .SingleAsync(cancellationToken);

            if (depth == 3)
            {
                buLeadOf = self.BusinessUnitId;
            }
            else
            {
                var group = await _db.Groups
                    .Where(g => g.Id == bu.GroupId)
                    .Select(g => new { g.PortfolioId })
                    .SingleAsync(cancellationToken);

                if (depth == 2)
                {
                    groupLeaderOf = bu.GroupId;
                }
                else // depth == 1
                {
                    portfolioLeaderOf = group.PortfolioId;
                }
            }
        }

        return new HierarchyRoles(isManager, portfolioLeaderOf, groupLeaderOf, buLeadOf);
    }

    /// <summary>Walks the manager chain upward from <paramref name="personId"/>, counting steps,
    /// until it either reaches a validated root (returns the step count) or runs out of chain
    /// (null manager on a non-root node, or an unresolved manager id — returns null: undefined
    /// depth, e.g. the null-manager directory-gap fixture, or a manager pointing outside the
    /// active population). A visited-set guards against a corrupt cyclic chain (shouldn't occur —
    /// DirectoryImportService rejects self-management and unresolved managers on import — but this
    /// resolver must never infinite-loop on data it did not validate itself).</summary>
    private static int? ComputeDepthFromRoot(
        Guid personId, IReadOnlyDictionary<Guid, PersonNode> byId, IReadOnlySet<Guid> roots)
    {
        var visited = new HashSet<Guid>();
        Guid? current = personId;
        int steps = 0;

        while (current is not null)
        {
            if (!visited.Add(current.Value))
            {
                return null; // cycle guard
            }

            if (roots.Contains(current.Value))
            {
                return steps;
            }

            if (!byId.TryGetValue(current.Value, out var node))
            {
                return null; // manager resolves outside the loaded (active) population
            }

            current = node.ManagerPersonId;
            steps++;
        }

        return null; // reached the top of the chain without hitting a validated root
    }
}
