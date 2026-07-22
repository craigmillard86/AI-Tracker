namespace Hap.Infrastructure.Cycles;

/// <summary>
/// The close-time work that <see cref="CycleService.CloseAsync"/> hooks in AFTER the Open → Closed
/// transition, within the SAME transaction (Q-017): auto-adoption of unmoderated submissions (FR-068),
/// the per-node rollup snapshots (research D4), and the frozen suppression verdicts (research D2).
///
/// <para><b>Why an interface here rather than inline in <see cref="CycleService"/>.</b> That work must
/// read moderated maturity scores, which — by the visibility-seam boundary (research D1, enforced by a
/// build-failing source scan) — may only be touched inside <c>Hap.Api/Authorization</c>. This service
/// lives in <c>Hap.Infrastructure</c> and therefore cannot. So the port is declared here (naming no
/// seam-guarded type) and its single implementation lives in the seam and is injected. The implementation
/// enrols on the SAME request-scoped <c>HapDbContext</c>, so the caller's ambient transaction covers every
/// write it stages; it must NOT open its own transaction or commit.</para>
/// </summary>
public interface ICycleCloseProcessor
{
    /// <summary>Run auto-adoption + snapshotting for the just-closed cycle, staging all writes on the
    /// shared context (the caller commits). Invoked exactly once per close, after the state transition.</summary>
    Task RunAsync(Guid cycleId, CancellationToken cancellationToken = default);
}
