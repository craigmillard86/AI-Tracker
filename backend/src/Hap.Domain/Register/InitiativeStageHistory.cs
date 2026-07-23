namespace Hap.Domain.Register;

/// <summary>
/// One immutable stage-transition record for an initiative (data-model.md "InitiativeStageHistory";
/// FR-028). Append-only and forward-only, mirroring <see cref="Hap.Domain.Audit.AuditLog"/>'s pattern
/// exactly: every property is get-only and set exactly once through the constructor (which EF Core
/// binds during materialisation). There is no setter, so an UPDATE cannot be expressed in C#; there is
/// no method that mutates state and none that deletes. Combined with the architecture test asserting no
/// <c>DbSet&lt;InitiativeStageHistory&gt;</c> ever sees Update/Remove (mirroring
/// <c>AuditAppendOnlyTests</c>), the trail is write-once by construction.
///
/// <para><b>No DB trigger backstop, by design (this story).</b> <see cref="Hap.Domain.Audit.AuditLog"/>
/// and <see cref="Hap.Domain.Rollups.RollupSnapshot"/> both carry a migration-level Postgres trigger as
/// the real backstop, reserved for the L3 audit/GDPR/rollup pattern (constitution Art. VI). Stage
/// history is register data, not individual assessment data or the frozen rollup record — an L2 story
/// (§7 EF migrations/schema trigger). Here the EF mapping (no setters, no DbSet mutation call anywhere
/// in <c>backend/src</c>) plus the paired architecture test IS the guarantee; a DB trigger can be added
/// later without a data migration if the guarantee ever needs to be raised to L3 strength.</para>
///
/// <para><see cref="PriorStage"/> is null only for the initial <see cref="InitiativeStage.Idea"/> row
/// written alongside <c>Initiative.Create</c> (there is no "before" stage) — every subsequent row (every
/// row written by the forward-transition endpoint) carries the stage the initiative held immediately
/// before this one. Despite the data-model.md prose ("Retired rows capture prior_stage") reading as if
/// scoped to <see cref="InitiativeStage.Retired"/>, <see cref="PriorStage"/> is the general field: EVERY
/// non-initial row captures its own prior stage, and Retired is simply the stage most often read back out
/// of it (FR-064's "stage held when retired").</para>
/// </summary>
public sealed class InitiativeStageHistory
{
    public Guid Id { get; }
    public Guid InitiativeId { get; }
    public InitiativeStage Stage { get; }

    /// <summary>The stage held immediately before this transition; null only for the initial Idea row
    /// written at initiative creation.</summary>
    public InitiativeStage? PriorStage { get; }

    public DateTime EnteredAt { get; }
    public Guid EnteredBy { get; }

    /// <summary>Full-state constructor. Used both by callers (via <see cref="Create"/>) and by EF Core
    /// constructor binding — parameter names match property names by convention.</summary>
    public InitiativeStageHistory(
        Guid id,
        Guid initiativeId,
        InitiativeStage stage,
        InitiativeStage? priorStage,
        DateTime enteredAt,
        Guid enteredBy)
    {
        Id = id;
        InitiativeId = initiativeId;
        Stage = stage;
        PriorStage = priorStage;
        EnteredAt = enteredAt;
        EnteredBy = enteredBy;
    }

    /// <summary>Creates a new stage-history row stamped now (UTC) with a fresh id.</summary>
    public static InitiativeStageHistory Create(
        Guid initiativeId, InitiativeStage stage, InitiativeStage? priorStage, Guid enteredBy) =>
        new(Guid.NewGuid(), initiativeId, stage, priorStage, DateTime.UtcNow, enteredBy);
}
