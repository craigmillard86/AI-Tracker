namespace Hap.Domain.Assessments;

/// <summary>
/// One dimension's scores within an <see cref="Assessment"/> (data-model.md "AssessmentScore";
/// **one per assessment per dimension**). Retains the self-score alongside the moderated (manager)
/// score so calibration delta can be computed (FR-011); the moderated score of record is the manager
/// score, or the self-score copied on auto-adoption (FR-068). Relocated to <c>Hap.Domain</c> by HAP-8
/// with <see cref="Assessment"/>; queried only through the seam's store.
///
/// <para>Self fields are mutable (a self-assessment is edited across visits until submitted);
/// <see cref="SetSelf"/> is the only mutator this story uses. Manager fields
/// (<see cref="ManagerScore"/>/<see cref="ManagerComment"/>) are carried in the schema for HAP-9 and
/// never written here.</para>
/// </summary>
public sealed class AssessmentScore
{
    /// <summary>The valid inclusive score range per dimension (spec FR-007: 0–3).</summary>
    public const int MinScore = 0;
    public const int MaxScore = 3;

    public Guid Id { get; private set; }
    public Guid AssessmentId { get; private set; }
    public Guid DimensionId { get; private set; }
    public int SelfScore { get; private set; }
    public string? SelfEvidence { get; private set; }
    public int? ManagerScore { get; private set; }
    public string? ManagerComment { get; private set; }

    // EF materialisation constructor.
    private AssessmentScore()
    {
    }

    /// <summary>Creates a self-score row for a dimension. <paramref name="selfScore"/> is validated to
    /// 0–3 (<see cref="ScoreOutOfRangeException"/>); evidence is optional free text.</summary>
    public static AssessmentScore CreateSelf(Guid assessmentId, Guid dimensionId, int selfScore, string? selfEvidence)
    {
        Validate(selfScore, dimensionId);
        return new AssessmentScore
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessmentId,
            DimensionId = dimensionId,
            SelfScore = selfScore,
            SelfEvidence = NormaliseEvidence(selfEvidence),
        };
    }

    /// <summary>Updates the self score/evidence for this dimension (upsert on a return visit). Validated
    /// identically to creation.</summary>
    public void SetSelf(int selfScore, string? selfEvidence)
    {
        Validate(selfScore, DimensionId);
        SelfScore = selfScore;
        SelfEvidence = NormaliseEvidence(selfEvidence);
    }

    private static void Validate(int score, Guid dimensionId)
    {
        if (score is < MinScore or > MaxScore)
        {
            throw new ScoreOutOfRangeException(dimensionId, score);
        }
    }

    // Empty/whitespace evidence is stored as null, not "" — "no evidence" has one representation.
    private static string? NormaliseEvidence(string? evidence) =>
        string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim();
}

/// <summary>A self-score outside the 0–3 range was supplied for a dimension. The seam maps this to a
/// 422 at the API.</summary>
public sealed class ScoreOutOfRangeException : Exception
{
    public Guid DimensionId { get; }
    public int Score { get; }

    public ScoreOutOfRangeException(Guid dimensionId, int score)
        : base($"Score {score} for dimension {dimensionId} is out of range ({AssessmentScore.MinScore}–{AssessmentScore.MaxScore}).")
    {
        DimensionId = dimensionId;
        Score = score;
    }
}
