using Hap.Domain.Assessments;

namespace Hap.Api.Authorization;

/// <summary>
/// The data-access port for assessment storage. The <see cref="Assessment"/>/<see cref="AssessmentScore"/>
/// entities now live in <c>Hap.Domain.Assessments</c> (HAP-8 relocation, HAP-5 handoff) so the database
/// context can map them; this port — and its single implementation — remain the ONLY code permitted to
/// query those tables (research D1, enforced by <c>SeamBoundaryTests</c>: the <c>DbSet</c>/<c>Set&lt;&gt;</c>
/// query surface may appear only inside the seam). Every read passes an access decision at
/// <see cref="AssessmentReads"/> first; the self-scope operations are invoked only by
/// <see cref="SelfAssessmentService"/>, whose caller is always the subject.
/// </summary>
public interface IAssessmentStore
{
    /// <summary>Cross-person individual read, invoked by <see cref="AssessmentReads"/> AFTER an access
    /// decision (never called directly by an endpoint).</summary>
    Task<IReadOnlyList<AssessmentScore>> GetIndividualScoresAsync(
        Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The self-scope slice of assessment storage, invoked only by <see cref="SelfAssessmentService"/>
/// (caller == subject). Segregated from <see cref="IAssessmentStore"/> so the cross-person read gateway
/// and its test doubles stay minimal; both interfaces are implemented by the single seam store
/// (<see cref="SeamAssessmentStore"/>), which remains the ONLY type that touches the assessment tables.
/// </summary>
public interface ISelfAssessmentStore
{
    /// <summary>The caller's own assessment for a cycle, with its scores — or null if they have not yet
    /// entered anything this cycle (no persisted row).</summary>
    Task<AssessmentWithScores?> GetSelfAsync(
        Guid personId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own self-scores for a (prior) cycle, keyed by dimension — the FR-062
    /// pre-population source. Empty when the person had no assessment that cycle.</summary>
    Task<IReadOnlyList<AssessmentScore>> GetSelfScoresForCycleAsync(
        Guid personId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>Upserts the caller's own self scores/evidence for a cycle, creating the assessment on
    /// first write (<see cref="AssessmentState.InProgress"/>). Rejects a write to an already-submitted
    /// assessment (<see cref="AssessmentAlreadySubmittedException"/>). The cycle-lock decision is the
    /// caller's (<see cref="SelfAssessmentService"/>) responsibility, made before this is invoked.</summary>
    Task UpsertSelfScoresAsync(
        Guid personId, Guid cycleId, IReadOnlyList<SelfScoreInput> scores, CancellationToken cancellationToken = default);

    /// <summary>Submits the caller's own assessment for a cycle (InProgress → Submitted). Rejects if no
    /// assessment exists, if it is already submitted (<see cref="AssessmentAlreadySubmittedException"/>),
    /// or if any required dimension is unscored (<see cref="AssessmentIncompleteException"/>).</summary>
    Task SubmitSelfAsync(
        Guid personId, Guid cycleId, IReadOnlyCollection<Guid> requiredDimensionIds, CancellationToken cancellationToken = default);
}

/// <summary>An assessment together with its per-dimension scores (a self-scope read result).</summary>
public sealed record AssessmentWithScores(Assessment Assessment, IReadOnlyList<AssessmentScore> Scores);

/// <summary>One dimension's self input on an upsert: the dimension, the 0–3 level, optional evidence.</summary>
public sealed record SelfScoreInput(Guid DimensionId, int Score, string? Evidence);

/// <summary>Attempted to write to or re-submit an assessment that is already
/// <see cref="AssessmentState.Submitted"/> (API 409).</summary>
public sealed class AssessmentAlreadySubmittedException : Exception
{
    public AssessmentAlreadySubmittedException(Guid personId, Guid cycleId)
        : base($"The assessment for person {personId} in cycle {cycleId} is already submitted and cannot be modified.")
    {
    }
}

/// <summary>Attempted to submit before every dimension of the cycle's framework has a self score
/// (API 422).</summary>
public sealed class AssessmentIncompleteException : Exception
{
    public AssessmentIncompleteException(int scored, int required)
        : base($"The assessment is incomplete: {scored} of {required} dimensions scored — all dimensions must be scored before submitting.")
    {
    }
}

/// <summary>Attempted a self read/write when no assessment cycle exists to act on — neither an Open
/// cycle nor a recently-Closed one in its late-override window (API 404 on read, 409 on write).</summary>
public sealed class NoCurrentCycleException : Exception
{
    public NoCurrentCycleException()
        : base("No assessment cycle is currently available.")
    {
    }
}

/// <summary>Attempted a self write against a cycle that does not currently accept submissions — Closed
/// without a late override, or Draft (Q-017a submission lock; API 423 Locked).</summary>
public sealed class AssessmentCycleLockedException : Exception
{
    public AssessmentCycleLockedException(Guid cycleId)
        : base($"Cycle {cycleId} does not currently accept submissions (closed without a late override).")
    {
    }
}

/// <summary>A supplied self score was outside the valid 0–3 range (API 422). The seam surfaces this so
/// endpoints outside the seam folder never need a dependency on the domain's own range exception (which
/// lives in a namespace the boundary guard forbids them to name).</summary>
public sealed class SelfScoreRangeException : Exception
{
    public SelfScoreRangeException(Guid dimensionId, int score)
        : base($"Score {score} for dimension {dimensionId} is out of range (0–3).")
    {
    }
}

/// <summary>The caller is not an invited, non-excluded participant of the resolved cycle — a contractor
/// excluded under FR-005, a not-onboarded-BU person (FR-002/FR-003), or someone with no invitation row
/// at all. Rejected as 404 on both read and write (symmetric with "no cycle": a non-participant has no
/// assessment to act on, and the person-addressed 404 convention avoids leaking participation).</summary>
public sealed class NotInvitedToCycleException : Exception
{
    public NotInvitedToCycleException(Guid cycleId, Guid personId)
        : base($"Person {personId} is not an invited participant of cycle {cycleId}.")
    {
    }
}

/// <summary>A score-write payload referenced a dimension that is not part of the resolved cycle's
/// framework version, or named the same dimension more than once (API 422). Caught in the seam BEFORE
/// any write, so an unknown dimension never reaches storage (which would 500 on the FK, or — once a v2
/// framework exists — silently persist a phantom score invisible to GET but ingested by rollups).</summary>
public sealed class SelfScoreDimensionException : Exception
{
    public SelfScoreDimensionException(string reason)
        : base(reason)
    {
    }
}
