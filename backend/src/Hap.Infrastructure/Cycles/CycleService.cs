using Hap.Domain.Cycles;
using Hap.Domain.Frameworks;
using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Cycles;

/// <summary>Counts from one <see cref="CycleService.OpenAsync"/> call, for the endpoint response
/// and test assertions (HAP-7 AC 1: "counts asserted against synth data").</summary>
public sealed record CycleOpenResult(
    Cycle Cycle, int TotalActivePeople, int Invited, int ExcludedContractor, int ExcludedNotOnboarded);

/// <summary>The referenced FrameworkVersion does not exist. Maps to 404 at the API.</summary>
public sealed class FrameworkVersionNotFoundException : Exception
{
    public Guid FrameworkVersionId { get; }

    public FrameworkVersionNotFoundException(Guid frameworkVersionId)
        : base($"FrameworkVersion {frameworkVersionId} does not exist.") =>
        FrameworkVersionId = frameworkVersionId;
}

/// <summary>
/// Orchestrates cycle create/open/close and late-override (contracts/api.md "[PA] POST
/// /api/cycles…"; FR-002/003/004/005/006/060). Split from the <see cref="Cycle"/> entity because
/// two of its jobs need database-wide visibility the entity itself deliberately does not have (see
/// <see cref="Cycle"/>'s class doc): the "one Open cycle per framework" cross-row check, and
/// invitation generation over the whole active-person population at open.
///
/// <para><b>Invitation generation (FR-003) writes one <see cref="CycleInvitation"/> row per ACTIVE
/// person</b>, not just those in onboarded BUs — see QUESTIONS.md Q-016 for why "onboarded BUs
/// mapped to the framework" is read as every onboarded BU in this single-framework build, and why
/// giving every active person a row (rather than only onboarded-BU ones) is what makes
/// <see cref="InvitationExclusionReason.NotOnboarded"/> meaningful, matching data-model.md's
/// excluded_reason enum. This runs ONCE, at open, inside the same transaction as the state
/// transition — never recomputed afterwards, which is exactly why a BU onboarded mid-Open gets no
/// invitations until the next open (FR-002 test).</para>
///
/// <para><b>Late override (FR-002) scope-checking is NOT this service's job.</b> "Platform Admin:
/// any person; Manager: own directs only" is an authorization decision made by the caller (the
/// endpoint, using the HAP-5 seam's <c>OrgGraphLoader</c>) BEFORE <see cref="GrantLateOverrideAsync"/>
/// is called — this service only persists the grant once scope has already been proven.</para>
/// </summary>
public sealed class CycleService
{
    private readonly HapDbContext _db;

    public CycleService(HapDbContext db) => _db = db;

    /// <summary>Creates a Draft cycle against an existing, non-retired framework version. No
    /// uniqueness check happens here — "one Open per framework" is enforced at <see cref="OpenAsync"/>,
    /// which is the only place a Draft cycle's state actually starts competing with another.</summary>
    public async Task<Cycle> CreateAsync(
        Guid frameworkVersionId, string name, bool contractorExclusionEnabled, CancellationToken ct = default)
    {
        var versionExists = await _db.FrameworkVersions.AnyAsync(v => v.Id == frameworkVersionId, ct);
        if (!versionExists)
        {
            throw new FrameworkVersionNotFoundException(frameworkVersionId);
        }

        var cycle = Cycle.Create(frameworkVersionId, name, contractorExclusionEnabled);
        _db.Cycles.Add(cycle);
        await _db.SaveChangesAsync(ct);
        return cycle;
    }

    /// <summary>
    /// Draft → Open (FR-002/003/060). Enforces "one Open cycle per framework" (409 via
    /// <see cref="DuplicateOpenCycleException"/> if violated), locks the adopted FrameworkVersion
    /// (FR-054 — idempotent if some other cycle already locked it), and generates the cycle's
    /// invitation snapshot: one <see cref="CycleInvitation"/> row per currently-active person,
    /// invited or excluded-with-reason per FR-003/005. All in one transaction.
    /// </summary>
    public async Task<CycleOpenResult> OpenAsync(Guid cycleId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var cycle = await _db.Cycles.FindAsync(new object[] { cycleId }, ct)
            ?? throw new CycleNotFoundException(cycleId);
        var version = await _db.FrameworkVersions.SingleAsync(v => v.Id == cycle.FrameworkVersionId, ct);

        // L2 panel round-1 note (advisory, not built): this is check-then-act with no DB-level
        // uniqueness constraint, so two concurrent admins racing to open two different Draft cycles
        // for the same framework could both pass this check before either commits. Low-risk for the
        // single-admin local build; a future story adding multi-admin concurrency should add a
        // partial unique index (e.g. on FrameworkId where State = 'Open', via a computed column or
        // a trigger, since FrameworkId itself lives on FrameworkVersion, not Cycle) rather than
        // relying on this check alone.
        var anotherOpenForFramework = await _db.Cycles
            .Where(c => c.Id != cycleId && c.State == CycleState.Open)
            .Join(_db.FrameworkVersions, c => c.FrameworkVersionId, v => v.Id, (c, v) => v.FrameworkId)
            .AnyAsync(frameworkId => frameworkId == version.FrameworkId, ct);
        if (anotherOpenForFramework)
        {
            throw new DuplicateOpenCycleException(version.FrameworkId);
        }

        cycle.Open(); // throws CycleStateTransitionException if not currently Draft (409)
        version.Lock(); // FR-054 — idempotent no-op if already locked by an earlier cycle

        var people = await _db.People.Where(p => p.IsActive).ToListAsync(ct);
        var onboardedByBu = await _db.BusinessUnits
            .ToDictionaryAsync(b => b.Id, b => b.IsOnboarded, ct);

        var invited = 0;
        var excludedContractor = 0;
        var excludedNotOnboarded = 0;
        var invitations = new List<CycleInvitation>(people.Count);

        foreach (var person in people)
        {
            var isOnboarded = onboardedByBu.TryGetValue(person.BusinessUnitId, out var onboarded) && onboarded;
            if (!isOnboarded)
            {
                invitations.Add(CycleInvitation.ExcludedFor(cycle.Id, person.Id, InvitationExclusionReason.NotOnboarded));
                excludedNotOnboarded++;
            }
            else if (person.EmployeeType == EmployeeType.Contractor && cycle.ContractorExclusionEnabled)
            {
                invitations.Add(CycleInvitation.ExcludedFor(cycle.Id, person.Id, InvitationExclusionReason.Contractor));
                excludedContractor++;
            }
            else
            {
                invitations.Add(CycleInvitation.Invited(cycle.Id, person.Id));
                invited++;
            }
        }

        _db.CycleInvitations.AddRange(invitations);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CycleOpenResult(cycle, people.Count, invited, excludedContractor, excludedNotOnboarded);
    }

    /// <summary>Open → Closed only (forward-only; throws <see cref="CycleStateTransitionException"/>
    /// otherwise — 409). No auto-adoption, rollups, or notifications here: those are HAP-10/HAP-18.
    /// contracts/api.md documents close as running auto-adoption (FR-068) + snapshots + suppression
    /// (research D2/D4) — HAP-10 MUST hook that work into THIS method (or a wrapper that still
    /// calls through it for the state transition) rather than building a parallel close path, so
    /// the Open→Closed transition and its side effects stay atomic and this handoff isn't silently
    /// dropped the way the late-override consult obligation almost was (QUESTIONS.md Q-017a/addendum).</summary>
    public async Task<Cycle> CloseAsync(Guid cycleId, CancellationToken ct = default)
    {
        var cycle = await _db.Cycles.FindAsync(new object[] { cycleId }, ct)
            ?? throw new CycleNotFoundException(cycleId);

        cycle.Close();
        await _db.SaveChangesAsync(ct);
        return cycle;
    }

    /// <summary>Persists a late-override grant for (cycle, person). Idempotent: a repeat grant for
    /// the same pair returns the existing row rather than duplicating it. The caller MUST have
    /// already proven scope (Platform Admin, or the target's direct manager) before calling this —
    /// see class doc.</summary>
    public async Task<CycleLateOverride> GrantLateOverrideAsync(
        Guid cycleId, Guid targetPersonId, Guid grantedByPersonId, string grantedByRole, CancellationToken ct = default)
    {
        var cycleExists = await _db.Cycles.AnyAsync(c => c.Id == cycleId, ct);
        if (!cycleExists)
        {
            throw new CycleNotFoundException(cycleId);
        }

        var existing = await _db.CycleLateOverrides
            .SingleOrDefaultAsync(o => o.CycleId == cycleId && o.PersonId == targetPersonId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var grant = CycleLateOverride.Create(cycleId, targetPersonId, grantedByPersonId, grantedByRole);
        _db.CycleLateOverrides.Add(grant);
        await _db.SaveChangesAsync(ct);
        return grant;
    }

    /// <summary>Whether a late-override row exists for (cycle, person) — the query
    /// <see cref="Cycle.AllowsSubmission"/>'s <c>hasLateOverride</c> parameter answers (QUESTIONS.md
    /// Q-017a: HAP-8/HAP-9 must call this when they build the real submission/moderation writes).</summary>
    public Task<bool> HasLateOverrideAsync(Guid cycleId, Guid personId, CancellationToken ct = default) =>
        _db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == personId, ct);
}
