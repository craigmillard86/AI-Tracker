using Hap.Domain.Assessments;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>
/// HAP-8 domain coverage: the <see cref="Assessment"/> forward-only state machine
/// (NotStarted → InProgress → Submitted) and <see cref="AssessmentScore"/> self-score validation +
/// upsert (FR-007: 0–3 per dimension, optional evidence). The cycle-lock, seam-only storage, and
/// endpoint behaviour are integration-tested; this pins the pure domain rules.
/// </summary>
public class AssessmentEntityTests
{
    [Fact]
    public void Start_opens_an_assessment_InProgress_for_the_person_and_cycle()
    {
        var cycleId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        var assessment = Assessment.Start(cycleId, personId);

        Assert.Equal(AssessmentState.InProgress, assessment.State);
        Assert.Equal(cycleId, assessment.CycleId);
        Assert.Equal(personId, assessment.PersonId);
        Assert.Null(assessment.SubmittedAt);
        Assert.False(assessment.Unmoderated);
    }

    [Fact]
    public void Submit_transitions_InProgress_to_Submitted_and_stamps_SubmittedAt()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());

        assessment.Submit();

        Assert.Equal(AssessmentState.Submitted, assessment.State);
        Assert.NotNull(assessment.SubmittedAt);
    }

    [Fact]
    public void Submit_twice_throws_the_forward_only_state_exception()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.Submit());
        Assert.Equal(AssessmentState.Submitted, ex.From);
        Assert.Equal(AssessmentState.Submitted, ex.To);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CreateSelf_accepts_scores_in_range_and_trims_evidence(int score)
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), score, "  evidence  ");

        Assert.Equal(score, row.SelfScore);
        Assert.Equal("evidence", row.SelfEvidence);
    }

    [Fact]
    public void CreateSelf_stores_blank_evidence_as_null()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 1, "   ");

        Assert.Null(row.SelfEvidence);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void CreateSelf_rejects_an_out_of_range_score(int score)
    {
        var dimensionId = Guid.NewGuid();

        var ex = Assert.Throws<ScoreOutOfRangeException>(
            () => AssessmentScore.CreateSelf(Guid.NewGuid(), dimensionId, score, null));
        Assert.Equal(dimensionId, ex.DimensionId);
        Assert.Equal(score, ex.Score);
    }

    [Fact]
    public void SetSelf_updates_an_existing_row_and_revalidates_range()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 1, "first");

        row.SetSelf(2, "second");
        Assert.Equal(2, row.SelfScore);
        Assert.Equal("second", row.SelfEvidence);

        Assert.Throws<ScoreOutOfRangeException>(() => row.SetSelf(9, null));
    }

    // --- HAP-9: moderation state transition -------------------------------------------------------

    [Fact]
    public void Moderate_transitions_Submitted_to_Moderated_and_records_moderator_and_instant()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();
        var moderator = Guid.NewGuid();

        assessment.Moderate(moderator);

        Assert.Equal(AssessmentState.Moderated, assessment.State);
        Assert.Equal(moderator, assessment.ModeratedByPersonId);
        Assert.NotNull(assessment.ModeratedAt);
    }

    [Fact]
    public void Moderate_before_submit_throws_the_forward_only_state_exception()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid()); // still InProgress

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.Moderate(Guid.NewGuid()));
        Assert.Equal(AssessmentState.InProgress, ex.From);
        Assert.Equal(AssessmentState.Moderated, ex.To);
    }

    [Fact]
    public void Moderate_twice_throws_a_report_cannot_be_re_moderated()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();
        assessment.Moderate(Guid.NewGuid());

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.Moderate(Guid.NewGuid()));
        Assert.Equal(AssessmentState.Moderated, ex.From);
    }

    // --- HAP-9: manager scoring + the FR-009 comment-at-Δ≥2 invariant -----------------------------

    [Theory]
    [InlineData(2, 2)] // Δ0
    [InlineData(2, 1)] // Δ1
    [InlineData(2, 3)] // Δ1
    public void SetManager_below_the_divergence_threshold_needs_no_comment(int self, int manager)
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), self, null);

        row.SetManager(manager, null); // no throw

        Assert.Equal(manager, row.ManagerScore);
        Assert.Equal(self, row.SelfScore); // self retained for the calibration delta (FR-011)
        Assert.Equal(Math.Abs(self - manager), row.Divergence);
    }

    [Theory]
    [InlineData(3, 1)] // Δ2
    [InlineData(0, 3)] // Δ3
    public void SetManager_at_or_above_the_divergence_threshold_requires_a_comment(int self, int manager)
    {
        var dimensionId = Guid.NewGuid();
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), dimensionId, self, null);

        var ex = Assert.Throws<ManagerCommentRequiredException>(() => row.SetManager(manager, null));
        Assert.Equal(dimensionId, ex.DimensionId);
        Assert.Null(row.ManagerScore); // nothing applied on rejection

        // A whitespace-only comment does not satisfy the requirement either.
        Assert.Throws<ManagerCommentRequiredException>(() => row.SetManager(manager, "   "));

        // With a real comment the diverging moderation is accepted.
        row.SetManager(manager, "evidence shows a lower level");
        Assert.Equal(manager, row.ManagerScore);
        Assert.Equal("evidence shows a lower level", row.ManagerComment);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void SetManager_rejects_an_out_of_range_score(int managerScore)
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 2, null);

        Assert.Throws<ScoreOutOfRangeException>(() => row.SetManager(managerScore, "comment"));
    }

    [Fact]
    public void Divergence_is_null_until_a_manager_score_is_set()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 2, null);

        Assert.Null(row.Divergence);
    }

    // --- HAP-12: retention erasure (FR-052) + the erased-row moderation guard (B1 riding fix) ------

    [Fact]
    public void Erase_nulls_the_raw_values_and_flags_the_row_erased()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 3, "sensitive evidence");
        row.SetManager(1, "moderated to L1");

        row.Erase();

        Assert.True(row.Erased);
        Assert.Equal(0, row.SelfScore);   // zeroed (non-nullable int; Q-027)
        Assert.Null(row.SelfEvidence);
        Assert.Null(row.ManagerScore);
        Assert.Null(row.ManagerComment);
    }

    [Fact]
    public void SetManager_on_an_erased_row_is_refused()
    {
        var dimensionId = Guid.NewGuid();
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), dimensionId, 3, "evidence");
        row.Erase();

        // A late-override re-moderation must never compute FR-009 against the fabricated-0 self-score.
        var ex = Assert.Throws<AssessmentScoreErasedException>(() => row.SetManager(2, "comment"));
        Assert.Equal(dimensionId, ex.DimensionId);
        Assert.Null(row.ManagerScore); // nothing applied
    }

    [Fact]
    public void AdoptSelf_on_an_erased_row_is_refused()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 2, null);
        row.Erase();

        Assert.Throws<AssessmentScoreErasedException>(() => row.AdoptSelf());
        Assert.Null(row.ManagerScore);
    }

    [Fact]
    public void SetSelf_on_an_erased_row_is_refused()
    {
        var dimensionId = Guid.NewGuid();
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), dimensionId, 2, "evidence");
        row.Erase();

        // Uniform guard across all three score mutators — a dormant-platform late override must not let a
        // self-score be re-entered into an erased row (the same-unit-of-work backstop).
        var ex = Assert.Throws<AssessmentScoreErasedException>(() => row.SetSelf(3, "new evidence"));
        Assert.Equal(dimensionId, ex.DimensionId);
        Assert.Equal(0, row.SelfScore);   // erased value stands
        Assert.Null(row.SelfEvidence);
    }
}
