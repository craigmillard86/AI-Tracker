using Hap.Domain.Cycles;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>
/// HAP-7 domain coverage: the <see cref="Cycle"/> forward-only state machine (Draft → Open →
/// Closed; FR-002/FR-060) and the pure <see cref="Cycle.AllowsSubmission"/> lock primitive
/// (QUESTIONS.md Q-017a — the actual submission write path is HAP-8/HAP-9's, this only proves the
/// rule those stories must consult). <see cref="CycleInvitation"/>'s two factories are exercised
/// separately: the invited/excluded shape is what cycle-open invitation generation
/// (Hap.Infrastructure.Cycles.CycleService, integration-tested) builds per person.
/// </summary>
public class CycleEntityTests
{
    private static Cycle NewDraftCycle(bool contractorExclusionEnabled = true) =>
        Cycle.Create(Guid.NewGuid(), "2026-08", contractorExclusionEnabled);

    [Fact]
    public void New_cycle_starts_Draft_with_no_open_or_close_timestamps()
    {
        var cycle = NewDraftCycle();

        Assert.Equal(CycleState.Draft, cycle.State);
        Assert.Null(cycle.OpensAt);
        Assert.Null(cycle.ClosesAt);
        Assert.Equal("2026-08", cycle.Name);
    }

    [Fact]
    public void Open_transitions_Draft_to_Open_and_sets_OpensAt()
    {
        var cycle = NewDraftCycle();

        cycle.Open();

        Assert.Equal(CycleState.Open, cycle.State);
        Assert.NotNull(cycle.OpensAt);
        Assert.Null(cycle.ClosesAt);
    }

    [Fact]
    public void Open_twice_is_rejected_forward_only()
    {
        var cycle = NewDraftCycle();
        cycle.Open();

        var ex = Assert.Throws<CycleStateTransitionException>(() => cycle.Open());
        Assert.Equal(CycleState.Open, ex.FromState);
        Assert.Equal(CycleState.Open, ex.AttemptedState);
    }

    [Fact]
    public void Close_transitions_Open_to_Closed_and_sets_ClosesAt()
    {
        var cycle = NewDraftCycle();
        cycle.Open();

        cycle.Close();

        Assert.Equal(CycleState.Closed, cycle.State);
        Assert.NotNull(cycle.ClosesAt);
    }

    [Fact]
    public void Draft_to_Closed_is_rejected()
    {
        var cycle = NewDraftCycle();

        var ex = Assert.Throws<CycleStateTransitionException>(() => cycle.Close());
        Assert.Equal(CycleState.Draft, ex.FromState);
        Assert.Equal(CycleState.Closed, ex.AttemptedState);
    }

    [Fact]
    public void Closed_to_Open_is_rejected()
    {
        var cycle = NewDraftCycle();
        cycle.Open();
        cycle.Close();

        var ex = Assert.Throws<CycleStateTransitionException>(() => cycle.Open());
        Assert.Equal(CycleState.Closed, ex.FromState);
        Assert.Equal(CycleState.Open, ex.AttemptedState);
    }

    [Fact]
    public void Close_twice_is_rejected()
    {
        var cycle = NewDraftCycle();
        cycle.Open();
        cycle.Close();

        Assert.Throws<CycleStateTransitionException>(() => cycle.Close());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllowsSubmission_is_true_while_Open_regardless_of_override(bool hasLateOverride)
    {
        var cycle = NewDraftCycle();
        cycle.Open();

        Assert.True(cycle.AllowsSubmission(hasLateOverride));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllowsSubmission_is_false_while_Draft_regardless_of_override(bool hasLateOverride)
    {
        var cycle = NewDraftCycle();

        Assert.False(cycle.AllowsSubmission(hasLateOverride));
    }

    [Fact]
    public void AllowsSubmission_is_false_once_Closed_without_a_late_override()
    {
        var cycle = NewDraftCycle();
        cycle.Open();
        cycle.Close();

        Assert.False(cycle.AllowsSubmission(hasLateOverride: false));
    }

    [Fact]
    public void AllowsSubmission_is_true_once_Closed_with_a_late_override()
    {
        var cycle = NewDraftCycle();
        cycle.Open();
        cycle.Close();

        Assert.True(cycle.AllowsSubmission(hasLateOverride: true));
    }

    [Fact]
    public void ContractorExclusionEnabled_defaults_from_creation_and_is_read_only()
    {
        var enabled = NewDraftCycle(contractorExclusionEnabled: true);
        var disabled = NewDraftCycle(contractorExclusionEnabled: false);

        Assert.True(enabled.ContractorExclusionEnabled);
        Assert.False(disabled.ContractorExclusionEnabled);
    }

    [Fact]
    public void Invited_factory_sets_InvitedAt_and_is_not_excluded()
    {
        var cycleId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        var invitation = CycleInvitation.Invited(cycleId, personId);

        Assert.Equal(cycleId, invitation.CycleId);
        Assert.Equal(personId, invitation.PersonId);
        Assert.NotNull(invitation.InvitedAt);
        Assert.False(invitation.Excluded);
        Assert.Null(invitation.ExcludedReason);
    }

    [Theory]
    [InlineData(InvitationExclusionReason.Contractor)]
    [InlineData(InvitationExclusionReason.NotOnboarded)]
    public void ExcludedFor_factory_records_the_reason_with_no_InvitedAt(InvitationExclusionReason reason)
    {
        var cycleId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        var invitation = CycleInvitation.ExcludedFor(cycleId, personId, reason);

        Assert.True(invitation.Excluded);
        Assert.Equal(reason, invitation.ExcludedReason);
        Assert.Null(invitation.InvitedAt);
    }

    [Fact]
    public void LateOverride_create_records_who_granted_it_and_for_whom()
    {
        var cycleId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();

        var grant = CycleLateOverride.Create(cycleId, personId, grantedBy, "Manager");

        Assert.Equal(cycleId, grant.CycleId);
        Assert.Equal(personId, grant.PersonId);
        Assert.Equal(grantedBy, grant.GrantedByPersonId);
        Assert.Equal("Manager", grant.GrantedByRole);
    }
}
