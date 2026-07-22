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
}
