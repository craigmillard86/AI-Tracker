using Hap.Domain.Assessments;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>HAP-10 domain rules for cycle-close auto-adoption (FR-068): the
/// <c>Submitted → AutoAdopted</c> transition on <see cref="Assessment"/> and the self→manager copy on
/// <see cref="AssessmentScore"/>. The close orchestration itself is integration-tested; this pins the
/// pure entity behaviour.</summary>
public class AutoAdoptEntityTests
{
    [Fact]
    public void AutoAdopt_transitions_Submitted_to_AutoAdopted_and_flags_unmoderated()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();

        assessment.AutoAdopt();

        Assert.Equal(AssessmentState.AutoAdopted, assessment.State);
        Assert.True(assessment.Unmoderated);
        Assert.Null(assessment.ModeratedByPersonId); // no moderator — there was none
    }

    [Fact]
    public void AutoAdopt_before_submit_throws_the_forward_only_state_exception()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid()); // InProgress

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.AutoAdopt());
        Assert.Equal(AssessmentState.InProgress, ex.From);
        Assert.Equal(AssessmentState.AutoAdopted, ex.To);
    }

    [Fact]
    public void A_moderated_assessment_can_never_be_auto_adopted()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();
        assessment.Moderate(Guid.NewGuid());

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.AutoAdopt());
        Assert.Equal(AssessmentState.Moderated, ex.From); // real moderation is never flipped to unmoderated
    }

    [Fact]
    public void An_auto_adopted_assessment_can_be_re_moderated_under_a_late_override_clearing_the_flag()
    {
        // Q-017a × FR-068: a late override granted AFTER close reopens moderation; by then close has
        // auto-adopted, so a real review must be able to replace the placeholder and clear unmoderated.
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();
        assessment.AutoAdopt();
        Assert.True(assessment.Unmoderated);

        var moderator = Guid.NewGuid();
        assessment.Moderate(moderator);

        Assert.Equal(AssessmentState.Moderated, assessment.State);
        Assert.False(assessment.Unmoderated); // placeholder superseded by a genuine review
        Assert.Equal(moderator, assessment.ModeratedByPersonId);
    }

    [Fact]
    public void A_moderated_assessment_can_never_be_re_moderated()
    {
        var assessment = Assessment.Start(Guid.NewGuid(), Guid.NewGuid());
        assessment.Submit();
        assessment.Moderate(Guid.NewGuid());

        var ex = Assert.Throws<AssessmentStateException>(() => assessment.Moderate(Guid.NewGuid()));
        Assert.Equal(AssessmentState.Moderated, ex.From); // only Submitted/AutoAdopted are moderatable
    }

    [Fact]
    public void AdoptSelf_copies_the_self_score_into_the_score_of_record_with_no_comment()
    {
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 3, "evidence");

        row.AdoptSelf();

        Assert.Equal(3, row.ManagerScore);
        Assert.Null(row.ManagerComment);
        Assert.Equal(0, row.Divergence); // definitionally zero — excluded from calibration (FR-068)
    }

    [Fact]
    public void AdoptSelf_never_trips_the_FR009_comment_rule_even_at_the_extreme()
    {
        // self = 3 copied to manager = 3 → Δ0, so no comment is ever required on the auto-adopt path.
        var row = AssessmentScore.CreateSelf(Guid.NewGuid(), Guid.NewGuid(), 0, null);
        row.AdoptSelf();
        Assert.Equal(0, row.ManagerScore);
        Assert.Null(row.ManagerComment);
    }
}
