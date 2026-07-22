using Hap.Domain.Cycles;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The seam's single source of truth for "which cycle does an assessment operation act on" and "what is
/// the prior cycle". Shared by <see cref="SelfAssessmentService"/> (self scope) and
/// <see cref="ManagerModerationService"/> (manager scope) so the two paths can NEVER drift on cycle
/// resolution — the same self-assessment and its moderation must resolve to the same cycle, and the same
/// prior cycle for FR-062/FR-063 pre-population/carry-forward (code-reviewer SHOULD-FIX A). Pure over the
/// <see cref="HapDbContext"/> it is handed; holds no state of its own.
/// </summary>
internal static class SeamCycleResolver
{
    /// <summary>
    /// The cycle a current assessment/moderation operation acts on: the single Open cycle if one exists
    /// (FR-002 — one Open per framework, single-framework build), otherwise the most-recently-opened
    /// Closed cycle (the post-close late-override / moderation window, Q-017a). A write against a Closed
    /// cycle is then gated by the submission lock. Draft cycles are never "current". Throws
    /// <see cref="NoCurrentCycleException"/> when neither an Open nor a Closed cycle exists.
    /// </summary>
    public static async Task<Cycle> CurrentCycleAsync(HapDbContext db, CancellationToken ct)
    {
        var open = await db.Cycles
            .Where(c => c.State == CycleState.Open)
            .OrderByDescending(c => c.OpensAt)
            .FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            return open;
        }

        var mostRecentClosed = await db.Cycles
            .Where(c => c.State == CycleState.Closed)
            .OrderByDescending(c => c.OpensAt)
            .FirstOrDefaultAsync(ct);
        return mostRecentClosed ?? throw new NoCurrentCycleException();
    }

    /// <summary>The immediately-preceding non-Draft cycle for the same framework version (FR-062/FR-063
    /// "previous cycle"), or null when there is none. Draft cycles never held scores, so they are
    /// excluded.</summary>
    public static async Task<Guid?> PriorCycleIdAsync(HapDbContext db, Cycle current, CancellationToken ct) =>
        await db.Cycles
            .Where(c => c.FrameworkVersionId == current.FrameworkVersionId
                        && c.Id != current.Id
                        && c.State != CycleState.Draft
                        && c.OpensAt < current.OpensAt)
            .OrderByDescending(c => c.OpensAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
}
