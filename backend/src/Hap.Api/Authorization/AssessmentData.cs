namespace Hap.Api.Authorization;

/// <summary>
/// SEAM-INTERNAL assessment entity. Defined HERE — inside <c>Hap.Api.Authorization</c>, the only
/// namespace permitted to name it (enforced by <c>SeamBoundaryTests</c>) — so that "no query path
/// reaches assessment data outside the seam" is a compile-time fact for this story.
///
/// <para>NO EF <c>DbSet</c> is registered and NO migration exists yet — deliberate: registering a
/// DbSet without its migration would fail verify's pending-model-change / idempotence gate. HAP-8 adds
/// the migration + DbSet and, at that point, relocates these types to <c>Hap.Domain</c> (so
/// <c>HapDbContext</c> in <c>Hap.Infrastructure</c> can register them without a layer inversion) and
/// extends the boundary guard to the DbSet form.</para>
/// </summary>
public sealed class Assessment
{
    public Guid Id { get; }

    /// <summary>The subject this assessment belongs to — the person whose individual scores these are.</summary>
    public Guid PersonId { get; }

    public Guid CycleId { get; }

    public Assessment(Guid id, Guid personId, Guid cycleId)
    {
        Id = id;
        PersonId = personId;
        CycleId = cycleId;
    }
}

/// <summary>SEAM-INTERNAL moderated/self score per dimension (spec Key Entities: AssessmentScore is
/// the source of truth for rollups, retaining the self-score for calibration delta). Same
/// no-DbSet-yet / seam-only rules as <see cref="Assessment"/>.</summary>
public sealed class AssessmentScore
{
    public Guid Id { get; }
    public Guid AssessmentId { get; }
    public Guid DimensionId { get; }
    public int SelfScore { get; }
    public int? ModeratedScore { get; }

    public AssessmentScore(Guid id, Guid assessmentId, Guid dimensionId, int selfScore, int? moderatedScore)
    {
        Id = id;
        AssessmentId = assessmentId;
        DimensionId = dimensionId;
        SelfScore = selfScore;
        ModeratedScore = moderatedScore;
    }
}

/// <summary>
/// The data-access port the gateway delegates to AFTER an access decision. HAP-8 implements this
/// against the (then-registered) DbSets; today a fake implementation lets the gateway's authorisation
/// be exhaustively unit-tested without a database. Living in the seam namespace, the port is itself
/// covered by the boundary guard, so the only assessment-storage contract in the codebase is the one
/// the gateway owns.
/// </summary>
public interface IAssessmentStore
{
    Task<IReadOnlyList<AssessmentScore>> GetIndividualScoresAsync(
        Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default);
}
