using Hap.Domain.Register;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>HAP-14 pure domain rules: the forward-only stage machine (<see cref="Initiative.AdvanceStage"/>,
/// FR-028), the weekly RAG update (<see cref="Initiative.PostWeeklyUpdate"/>, FR-033), NR line creation
/// validation (<see cref="InitiativeNRLine.Create"/>, FR-029), and the submission-lock stub
/// (<see cref="InitiativeNRLine.MarkReferencedBySubmission"/>). Endpoint wiring (history-row writes,
/// permission checks) is integration-tested; this pins the entity behaviour.</summary>
public class InitiativeStageAndUpdatesTests
{
    private static Initiative NewInitiative(Guid? categoryId = null, Guid? ownerId = null) =>
        Initiative.Create(
            businessUnitId: Guid.NewGuid(),
            name: "Claims Triage Copilot",
            description: "desc",
            sponsorPersonId: null,
            ownerPersonId: ownerId ?? Guid.NewGuid(),
            createdByPersonId: Guid.NewGuid(),
            categoryId: categoryId ?? Guid.NewGuid(),
            aiDlcLevel: 2,
            functionsAffected: null,
            dimensionsAdvanced: null,
            customersInProduction: null,
            riskTier: RiskTier.Low);

    // ---- AdvanceStage (FR-028) --------------------------------------------------------------------

    [Fact]
    public void AdvanceStage_walks_every_forward_transition_and_returns_the_prior_stage()
    {
        var initiative = NewInitiative();
        Assert.Equal(InitiativeStage.Idea, initiative.CurrentStage);

        Assert.Equal(InitiativeStage.Idea, initiative.AdvanceStage(InitiativeStage.Evaluation));
        Assert.Equal(InitiativeStage.Evaluation, initiative.CurrentStage);

        Assert.Equal(InitiativeStage.Evaluation, initiative.AdvanceStage(InitiativeStage.Pilot));
        Assert.Equal(InitiativeStage.Pilot, initiative.CurrentStage);

        Assert.Equal(InitiativeStage.Pilot, initiative.AdvanceStage(InitiativeStage.Production));
        Assert.Equal(InitiativeStage.Production, initiative.CurrentStage);

        Assert.Equal(InitiativeStage.Production, initiative.AdvanceStage(InitiativeStage.Scaled));
        Assert.Equal(InitiativeStage.Scaled, initiative.CurrentStage);

        Assert.Equal(InitiativeStage.Scaled, initiative.AdvanceStage(InitiativeStage.Retired));
        Assert.Equal(InitiativeStage.Retired, initiative.CurrentStage);
    }

    [Fact]
    public void AdvanceStage_allows_a_multi_step_forward_jump()
    {
        // "Forward-only" does not mean "one step at a time" — Idea straight to Pilot is intentional.
        var initiative = NewInitiative();

        var prior = initiative.AdvanceStage(InitiativeStage.Pilot);

        Assert.Equal(InitiativeStage.Idea, prior);
        Assert.Equal(InitiativeStage.Pilot, initiative.CurrentStage);
    }

    [Fact]
    public void AdvanceStage_rejects_a_backward_transition_with_409_mapped_exception()
    {
        var initiative = NewInitiative();
        initiative.AdvanceStage(InitiativeStage.Pilot);

        Assert.Throws<InitiativeStageTransitionException>(() => initiative.AdvanceStage(InitiativeStage.Evaluation));
        Assert.Equal(InitiativeStage.Pilot, initiative.CurrentStage); // unchanged on rejection
    }

    [Fact]
    public void AdvanceStage_rejects_a_same_stage_no_op()
    {
        var initiative = NewInitiative();
        initiative.AdvanceStage(InitiativeStage.Evaluation);

        Assert.Throws<InitiativeStageTransitionException>(() => initiative.AdvanceStage(InitiativeStage.Evaluation));
    }

    [Fact]
    public void AdvanceStage_from_retired_is_always_rejected_terminal()
    {
        var initiative = NewInitiative();
        initiative.AdvanceStage(InitiativeStage.Retired);

        // Even a "forward" target (there is none beyond Retired) must be rejected — Retired is terminal.
        var ex = Assert.Throws<InitiativeStageTransitionException>(() => initiative.AdvanceStage(InitiativeStage.Retired));
        Assert.Contains("terminal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- PostWeeklyUpdate (FR-033) -----------------------------------------------------------------

    [Fact]
    public void PostWeeklyUpdate_refreshes_rag_and_last_update_at()
    {
        var initiative = NewInitiative();
        var at = DateTime.UtcNow.AddDays(3);

        initiative.PostWeeklyUpdate(RagStatus.AtRisk, at);

        Assert.Equal(RagStatus.AtRisk, initiative.RagStatus);
        Assert.Equal(at, initiative.LastUpdateAt);
    }

    // ---- SetCustomersInProduction (FR-031 gating support) -------------------------------------------

    [Fact]
    public void SetCustomersInProduction_rejects_negative_values()
    {
        var initiative = NewInitiative();
        Assert.Throws<InitiativeValidationException>(() => initiative.SetCustomersInProduction(-1));
    }

    [Fact]
    public void SetCustomersInProduction_accepts_null_and_non_negative_values()
    {
        var initiative = NewInitiative();
        initiative.SetCustomersInProduction(5);
        Assert.Equal(5, initiative.CustomersInProduction);

        initiative.SetCustomersInProduction(null);
        Assert.Null(initiative.CustomersInProduction);
    }

    // ---- InitiativeNRLine.Create (FR-029) ------------------------------------------------------------

    [Fact]
    public void NRLine_Create_persists_all_fields_unreferenced_by_default()
    {
        var initiativeId = Guid.NewGuid();

        var line = InitiativeNRLine.Create(initiativeId, 2026, NRDirection.Direct, NRRecurrence.Recurring, 125_000m, "Claims automation savings");

        Assert.Equal(initiativeId, line.InitiativeId);
        Assert.Equal(2026, line.Year);
        Assert.Equal(NRDirection.Direct, line.Direction);
        Assert.Equal(NRRecurrence.Recurring, line.Recurrence);
        Assert.Equal(125_000m, line.AmountUsd);
        Assert.Equal("Claims automation savings", line.Description);
        Assert.Null(line.ReferencedBySubmissionLineId);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void NRLine_Create_rejects_a_year_outside_the_plausible_range(int year)
    {
        Assert.Throws<InitiativeValidationException>(
            () => InitiativeNRLine.Create(Guid.NewGuid(), year, NRDirection.Direct, NRRecurrence.OneTime, 100m, null));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    public void NRLine_Create_accepts_the_boundary_years(int year)
    {
        var line = InitiativeNRLine.Create(Guid.NewGuid(), year, NRDirection.Indirect, NRRecurrence.OneTime, 0m, null);
        Assert.Equal(year, line.Year);
    }

    [Fact]
    public void NRLine_Create_rejects_a_negative_amount()
    {
        Assert.Throws<InitiativeValidationException>(
            () => InitiativeNRLine.Create(Guid.NewGuid(), 2026, NRDirection.Direct, NRRecurrence.OneTime, -0.01m, null));
    }

    [Fact]
    public void NRLine_Create_accepts_a_zero_amount()
    {
        var line = InitiativeNRLine.Create(Guid.NewGuid(), 2026, NRDirection.Direct, NRRecurrence.OneTime, 0m, null);
        Assert.Equal(0m, line.AmountUsd);
    }

    [Fact]
    public void NRLine_Create_trims_and_nulls_blank_description()
    {
        var withWhitespace = InitiativeNRLine.Create(Guid.NewGuid(), 2026, NRDirection.Direct, NRRecurrence.OneTime, 10m, "  padded  ");
        Assert.Equal("padded", withWhitespace.Description);

        var blank = InitiativeNRLine.Create(Guid.NewGuid(), 2026, NRDirection.Direct, NRRecurrence.OneTime, 10m, "   ");
        Assert.Null(blank.Description);
    }

    // ---- MarkReferencedBySubmission (HAP-16 stub) ----------------------------------------------------

    [Fact]
    public void MarkReferencedBySubmission_sets_the_lock_reference()
    {
        var line = InitiativeNRLine.Create(Guid.NewGuid(), 2026, NRDirection.Direct, NRRecurrence.OneTime, 10m, null);
        var submissionLineId = Guid.NewGuid();

        line.MarkReferencedBySubmission(submissionLineId);

        Assert.Equal(submissionLineId, line.ReferencedBySubmissionLineId);
    }
}
