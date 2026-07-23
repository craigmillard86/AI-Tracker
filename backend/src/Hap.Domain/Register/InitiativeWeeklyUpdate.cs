namespace Hap.Domain.Register;

/// <summary>
/// One immutable weekly status entry for an initiative (data-model.md "InitiativeWeeklyUpdate";
/// FR-033/FR-037). Append-only, mirroring <see cref="InitiativeStageHistory"/> and
/// <see cref="Hap.Domain.Audit.AuditLog"/>'s pattern: every property is get-only, set exactly once
/// through the constructor EF binds to. No setter, no mutator, no delete — a correction is a new update
/// row, never an edit to a past one. The trail is what the detail screen renders newest-first (AC:
/// "update trail returned newest-first on the detail endpoint").
/// </summary>
public sealed class InitiativeWeeklyUpdate
{
    public Guid Id { get; }
    public Guid InitiativeId { get; }
    public RagStatus RagStatus { get; }

    /// <summary>The one-line status note (FR-033: "lightweight, &lt;1 min entry"). Optional — a bare
    /// RAG-only update is valid.</summary>
    public string? Note { get; }

    public Guid CreatedBy { get; }
    public DateTime CreatedAt { get; }

    /// <summary>Full-state constructor. Used both by callers (via <see cref="Create"/>) and by EF Core
    /// constructor binding — parameter names match property names by convention.</summary>
    public InitiativeWeeklyUpdate(
        Guid id,
        Guid initiativeId,
        RagStatus ragStatus,
        string? note,
        Guid createdBy,
        DateTime createdAt)
    {
        Id = id;
        InitiativeId = initiativeId;
        RagStatus = ragStatus;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    /// <summary>Creates a new weekly-update row with a fresh id, stamped with the caller-supplied
    /// instant. <paramref name="createdAt"/> is required (not self-stamped via <c>DateTime.UtcNow</c>)
    /// so the caller can pass the SAME instant it also stamps onto <see cref="Initiative.LastUpdateAt"/>
    /// via <see cref="Initiative.PostWeeklyUpdate"/> — two separate <c>UtcNow</c> calls for what is one
    /// logical event previously let the two timestamps drift apart (panel finding, HAP-14).</summary>
    public static InitiativeWeeklyUpdate Create(
        Guid initiativeId, RagStatus ragStatus, string? note, Guid createdBy, DateTime createdAt) =>
        new(Guid.NewGuid(), initiativeId, ragStatus, note, createdBy, createdAt);
}
