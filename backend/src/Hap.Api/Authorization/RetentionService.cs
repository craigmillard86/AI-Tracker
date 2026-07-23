using System.Text.Json;
using Hap.Domain.Audit;
using Hap.Domain.Cycles;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// GDPR retention job (contracts/api.md POST /api/admin/retention/run; FR-052; HAP-12). Lives in the seam
/// because it erases raw individual assessment values (seam-only write path). Policy: raw self/manager score
/// VALUES older than <see cref="RetentionYears"/> years are erased in place — the rows are RETAINED so the
/// frozen <c>RollupSnapshot</c> aggregates stay reconcilable and history is not perforated (FR-052:
/// "aggregates retained indefinitely"); snapshots are never touched.
///
/// <para><b>Age is measured by the owning cycle's close date</b> (<see cref="Cycle.ClosesAt"/>): a cycle
/// still Open or Draft is never in scope, and a cycle closed within the window keeps its raw values. This is
/// the honest interpretation of "raw individual scores retained 3 years" — the clock starts when the
/// assessment data was finalised at close.</para>
///
/// <para><b>Idempotent by construction (AC):</b> the authoritative "this assessment was already erased"
/// ledger is the per-assessment <see cref="AuditAction.RetentionErasure"/> audit row itself, not the row
/// content (which — for a zeroed <c>SelfScore</c>, Q-027 — is ambiguous with a genuine 0). A second run
/// resolves the same already-erased set from the audit log and the store erases zero further rows.</para>
/// </summary>
public sealed class RetentionService
{
    /// <summary>The GDPR raw-score retention period (FR-052): 3 years. A named constant so the policy is one
    /// value, not a scattered literal.</summary>
    public const int RetentionYears = 3;

    private readonly HapDbContext _db;
    private readonly IAssessmentStore _store;
    private readonly ErasureLedger _ledger;

    public RetentionService(HapDbContext db, IAssessmentStore store, ErasureLedger ledger)
    {
        _db = db;
        _store = store;
        _ledger = ledger;
    }

    /// <summary>Runs the retention erasure. <paramref name="runByPersonId"/> is the Platform Admin who
    /// triggered it — recorded as the actor on every <c>RetentionErasure</c> audit row (the subject is the
    /// person whose data was erased). <paramref name="asOf"/> defaults to now (UTC); it exists so tests can
    /// pin the clock. Returns the counts erased (both zero on an idempotent re-run).</summary>
    public async Task<RetentionErasureResult> RunAsync(
        Guid runByPersonId, DateTime? asOf = null, CancellationToken ct = default)
    {
        var cutoff = (asOf ?? DateTime.UtcNow).AddYears(-RetentionYears);

        // Cycles whose data is past retention: Closed, and closed before the cutoff. A never-closed cycle
        // (ClosesAt null) is excluded by the comparison.
        var oldCycleIds = await _db.Cycles
            .Where(c => c.State == CycleState.Closed && c.ClosesAt != null && c.ClosesAt < cutoff)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (oldCycleIds.Count == 0)
        {
            return new RetentionErasureResult(0, 0);
        }

        // The idempotency ledger: the set of assessment ids already carrying a RetentionErasure audit row
        // (the shared ErasureLedger — same source the display reads + the export consult).
        var alreadyErased = await _ledger.AllErasedAssessmentIdsAsync(ct);

        return await _store.RunRetentionErasureAsync(
            oldCycleIds,
            alreadyErased,
            assessment => AuditLog.Create(
                AuditAction.RetentionErasure,
                actorPersonId: runByPersonId,
                subjectPersonId: assessment.PersonId,
                detail: JsonSerializer.Serialize(new { assessmentId = assessment.Id, cycleId = assessment.CycleId })),
            ct);
    }

}
