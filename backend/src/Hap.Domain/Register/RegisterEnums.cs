namespace Hap.Domain.Register;

/// <summary>
/// An initiative's lifecycle stage (data-model.md "Initiative stage"; FR-028). Forward-only:
/// Idea → Evaluation → Pilot → Production → Scaled → Retired, with Retired terminal. HAP-13 only
/// ever sets <see cref="Idea"/> (at creation); the forward-only transition endpoint that walks the
/// rest is HAP-14. The enum carries every value now so the seeded <see cref="HarrisStageMap"/> can
/// map all six stages to their Harris stage (FR-064) without a later schema change.
/// </summary>
public enum InitiativeStage
{
    Idea,
    Evaluation,
    Pilot,
    Production,
    Scaled,
    Retired,
}

/// <summary>
/// The Harris AI Dashboard reporting stage an internal <see cref="InitiativeStage"/> rolls up to
/// (FR-064). The mapping itself is DATA — seeded into <see cref="HarrisStageMap"/> from
/// <c>docs/frameworks/harris-taxonomy.v1.json</c>, never hard-coded (constitution Art. II.4). This
/// enum only names the four possible target values.
/// </summary>
public enum HarrisStage
{
    Ideation,
    Development,
    Production,
    IdeasTriedButStopped,
}

/// <summary>An initiative's current delivery-health signal (FR-033). Labels mirror the register-list
/// mockup's RAG chips ("On Track" / "At Risk" / "Off Track"). Defaults to <see cref="OnTrack"/> at
/// creation; the weekly-update flow that changes it is HAP-14.</summary>
public enum RagStatus
{
    OnTrack,
    AtRisk,
    OffTrack,
}

/// <summary>The governance risk tier of an initiative (FR-030 — the one governance field in HAP-13's
/// scope). The wider governance panel (data sensitivity, regulatory relevance, approval, oversight)
/// is a later story.</summary>
public enum RiskTier
{
    Low,
    Med,
    High,
}
