namespace Hap.Domain.Assessments;

/// <summary>
/// One dimension's scores within an <see cref="Assessment"/> (data-model.md "AssessmentScore";
/// **one per assessment per dimension**). Retains the self-score alongside the moderated (manager)
/// score so calibration delta can be computed (FR-011); the moderated score of record is the manager
/// score, or the self-score copied on auto-adoption (FR-068). Relocated to <c>Hap.Domain</c> by HAP-8
/// with <see cref="Assessment"/>; queried only through the seam's store.
///
/// <para>Self fields are mutable across visits until submitted (<see cref="SetSelf"/>); the manager
/// fields (<see cref="ManagerScore"/>/<see cref="ManagerComment"/>) are written once, at moderation, by
/// HAP-9's <see cref="SetManager"/> — which also enforces the FR-009 comment-required-at-Δ≥2 invariant.
/// The self-score is retained untouched so the calibration delta (FR-011) survives moderation.</para>
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

    /// <summary>The divergence between the self and manager score for this dimension (FR-011 calibration
    /// signal; FR-012 divergence highlight). Undefined until moderated — null when no manager score is
    /// set.</summary>
    public int? Divergence => ManagerScore is int m ? Math.Abs(SelfScore - m) : null;

    /// <summary>
    /// Records the manager's moderated score/comment for this dimension (HAP-9; FR-008/009/010). The
    /// self-score is retained untouched for the calibration delta (FR-011). Enforces the data-model.md
    /// invariant <b>a manager comment is required when |self − manager| ≥ 2</b> (FR-009): a diverging
    /// moderation with no (or whitespace-only) comment throws <see cref="ManagerCommentRequiredException"/>,
    /// which the seam maps to a 422. <paramref name="managerScore"/> is range-validated 0–3 exactly like a
    /// self score. This is the ONLY mutator that writes the manager fields.
    /// </summary>
    public void SetManager(int managerScore, string? managerComment)
    {
        Validate(managerScore, DimensionId);

        var comment = NormaliseEvidence(managerComment);
        if (Math.Abs(SelfScore - managerScore) >= DivergenceCommentThreshold && comment is null)
        {
            throw new ManagerCommentRequiredException(DimensionId, SelfScore, managerScore);
        }

        ManagerScore = managerScore;
        ManagerComment = comment;
    }

    /// <summary>Cycle-close auto-adoption (FR-068): copies the self score into the moderated
    /// (<see cref="ManagerScore"/>) slot with NO comment. The divergence is definitionally zero, so this
    /// never trips the FR-009 comment rule; kept as a distinct, intention-revealing mutator (rather than
    /// <c>SetManager(SelfScore, null)</c>) so an auto-adopted score of record reads unambiguously as
    /// adopted-not-moderated, and so no accidental non-zero manager value can be passed on this path.</summary>
    public void AdoptSelf()
    {
        ManagerScore = SelfScore;
        ManagerComment = null;
    }

    /// <summary>The divergence (|self − manager|) at or above which a manager comment is mandatory
    /// (FR-009). A named constant so the domain rule and any UI hint that mirrors it cannot drift.</summary>
    public const int DivergenceCommentThreshold = 2;

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

/// <summary>A manager moderated a dimension with a divergence of ≥2 from the self-score but supplied no
/// comment, violating the FR-009 invariant (data-model.md: "manager_comment required when
/// |self − manager| ≥ 2"). The seam maps this to a 422.
///
/// <para><b>The message carries NO score values (L3 red-team, defense-in-depth).</b> The self and manager
/// scores are kept as structured properties for tests/diagnostics but MUST NEVER appear in the
/// client-visible text: an FR-009 error surfaced verbatim (the old "(self X, manager Y)" form) is a score
/// oracle — a caller probing manager scores could read back the subject's exact self-score, and even the
/// precise divergence value leaks it given the caller knows their own probe. The message states only the
/// dimension and the public threshold.</para></summary>
public sealed class ManagerCommentRequiredException : Exception
{
    public Guid DimensionId { get; }
    public int SelfScore { get; }
    public int ManagerScore { get; }

    public ManagerCommentRequiredException(Guid dimensionId, int selfScore, int managerScore)
        : base($"Dimension {dimensionId}: a manager comment is required when the moderated score diverges " +
               $"from the self-score by at least {AssessmentScore.DivergenceCommentThreshold} levels (FR-009).")
    {
        DimensionId = dimensionId;
        SelfScore = selfScore;
        ManagerScore = managerScore;
    }
}
