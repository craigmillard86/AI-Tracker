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

    /// <summary>A subject's assessment (with its per-dimension scores) for a cycle — the cross-person
    /// moderation-view read (HAP-9). Invoked by <see cref="ManagerModerationService"/> AFTER the caller
    /// has been authorised for the subject; null when the subject has no assessment row this cycle.</summary>
    Task<AssessmentWithScores?> GetAssessmentWithScoresAsync(
        Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>The assessment (with scores) addressed by <paramref name="assessmentId"/> — the
    /// moderation write resolves the subject/cycle/state from the assessment id in the route. Null when
    /// no such assessment exists. Authorisation happens at the service AFTER this resolves the subject.</summary>
    Task<AssessmentWithScores?> GetByIdWithScoresAsync(
        Guid assessmentId, CancellationToken cancellationToken = default);

    /// <summary>The assessment rows for a set of people in a cycle (the manager's review queue —
    /// state per report). No scores are returned (the queue shows state + flags only, no score data);
    /// people with no assessment row this cycle are simply absent from the result.</summary>
    Task<IReadOnlyList<Assessment>> GetAssessmentsForPeopleAsync(
        Guid cycleId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a manager's moderation to the assessment (HAP-9; FR-008/009/010). Atomic: every
    /// per-dimension manager score/comment (<paramref name="decisions"/>), the
    /// <c>Submitted → Moderated</c> transition (recording <paramref name="moderatedByPersonId"/>), and the
    /// staged <paramref name="auditRow"/> (the FR-050 <c>ScoreChange</c> record) commit together, or none
    /// do — an audit-write failure rolls the whole moderation back (fail-closed, mirrors
    /// <c>OrgOverrideService</c>). Throws <see cref="AssessmentNotFoundException"/> (→404) if the id is
    /// unknown, <see cref="AssessmentStateException"/> (→409) if it is not currently Submitted (checked
    /// BEFORE any score write so a wrong-state moderation never partially applies),
    /// <see cref="ModerationDimensionException"/> (→422) for a decision naming a dimension the assessment
    /// has no score row for, and the domain's <see cref="ManagerCommentRequiredException"/>/
    /// <see cref="ScoreOutOfRangeException"/> (→422) from <see cref="AssessmentScore.SetManager"/>.
    /// </summary>
    Task ModerateAsync(
        Guid assessmentId,
        Guid moderatedByPersonId,
        IReadOnlyList<ManagerScoreInput> decisions,
        Hap.Domain.Audit.AuditLog auditRow,
        CancellationToken cancellationToken = default);
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

/// <summary>One dimension's manager decision on a moderation: the dimension, the 0–3 moderated score,
/// and an optional comment (mandatory in the domain when |self − manager| ≥ 2, FR-009).</summary>
public sealed record ManagerScoreInput(Guid DimensionId, int Score, string? Comment);

/// <summary>The assessment addressed by a moderation write does not exist (API 404). Distinct from the
/// authorisation denial (also 404, existence-leak convention) so the two paths read clearly in code.</summary>
public sealed class AssessmentNotFoundException : Exception
{
    public AssessmentNotFoundException(Guid assessmentId)
        : base($"Assessment {assessmentId} does not exist.")
    {
    }
}

/// <summary>A moderation decision named a dimension the assessment has no score row for — an unknown or
/// foreign-framework dimension (API 422). Caught before the transaction commits, so no partial
/// moderation persists.</summary>
public sealed class ModerationDimensionException : Exception
{
    public ModerationDimensionException(Guid dimensionId)
        : base($"Dimension {dimensionId} is not part of this assessment — it has no self score to moderate.")
    {
    }
}

/// <summary>A moderation was attempted against an assessment that is not currently Submitted — nothing to
/// moderate, or already moderated/auto-adopted (API 409). The seam re-surfaces the domain's forward-only
/// state exception as its own type so the endpoint never references the domain assessment namespace (the
/// boundary guard forbids naming it outside the seam).</summary>
public sealed class ModerationNotSubmittedException : Exception
{
    public ModerationNotSubmittedException(Guid assessmentId)
        : base($"Assessment {assessmentId} is not awaiting moderation (it must be Submitted).")
    {
    }
}

/// <summary>A moderation decision was invalid (API 422): a divergence of ≥2 without a comment (FR-009),
/// or a manager score outside 0–3. The seam re-surfaces the domain's validation exceptions as its own
/// type — same reason as <see cref="ModerationNotSubmittedException"/> — carrying the domain message.</summary>
public sealed class ModerationValidationException : Exception
{
    public ModerationValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>A moderation lost an optimistic-concurrency race — another moderation of the same assessment
/// committed first (the assessment's <c>xmin</c> concurrency token moved). API 409. Distinct from
/// <see cref="ModerationNotSubmittedException"/> only for diagnostics; both are 409 conflicts.</summary>
public sealed class ModerationConflictException : Exception
{
    public ModerationConflictException(Guid assessmentId)
        : base($"Assessment {assessmentId} was moderated concurrently — retry against the current state.")
    {
    }
}

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
