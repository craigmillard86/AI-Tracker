using Hap.Domain.Register;

namespace Hap.Domain.Reporting;

/// <summary>
/// A BU's weekly AI-DLC level declaration (data-model.md "BUAIDLCDeclaration"; FR-047). One row per
/// BU per ISO week — <see cref="WeekOf"/> is always the Monday of that week, but this entity has no
/// calendar knowledge: normalisation to Monday happens at the endpoint layer (single-responsibility —
/// the entity just stores what it is given, mirroring how <see cref="Initiative"/> leaves category/
/// dimension-key validation to its caller).
///
/// <para><b>Upsert, not append (FR-047 amended 2026-07-21).</b> A same-week resubmission calls
/// <see cref="Update"/> rather than creating a new row — mirroring <see cref="Initiative"/>'s
/// Create/Edit split, this is the entity-level half of the endpoint's find-then-branch upsert. The
/// unique index on (BusinessUnitId, WeekOf) (<c>BuAiDlcDeclarationConfiguration</c>) is what makes that
/// upsert safe under concurrency.</para>
///
/// <para>Reuses <see cref="RagStatus"/> from <c>Hap.Domain.Register</c> rather than a duplicate
/// declaration-scoped enum — the RAG vocabulary (On Track / At Risk / Off Track) is the same concept in
/// both places.</para>
/// </summary>
public sealed class BuAiDlcDeclaration
{
    public Guid Id { get; private set; }
    public Guid BusinessUnitId { get; private set; }
    public DateOnly WeekOf { get; private set; }
    public int DeclaredLevel { get; private set; }
    public DateOnly? NextLevelExpectedDate { get; private set; }
    public RagStatus RagStatus { get; private set; }
    public string? Note { get; private set; }
    public Guid DeclaredBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // EF materialisation constructor (parameter names match property names by convention).
    private BuAiDlcDeclaration(
        Guid id,
        Guid businessUnitId,
        DateOnly weekOf,
        int declaredLevel,
        DateOnly? nextLevelExpectedDate,
        RagStatus ragStatus,
        string? note,
        Guid declaredBy,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        BusinessUnitId = businessUnitId;
        WeekOf = weekOf;
        DeclaredLevel = declaredLevel;
        NextLevelExpectedDate = nextLevelExpectedDate;
        RagStatus = ragStatus;
        Note = note;
        DeclaredBy = declaredBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Records a BU's first declaration for a given (already Monday-normalised) week. Validates
    /// <paramref name="declaredLevel"/> is 0–3 and that the BU/declaring-person ids are present,
    /// throwing <see cref="BuReportingValidationException"/> on violation.
    /// </summary>
    public static BuAiDlcDeclaration Create(
        Guid businessUnitId,
        DateOnly weekOf,
        int declaredLevel,
        DateOnly? nextLevelExpectedDate,
        RagStatus ragStatus,
        string? note,
        Guid declaredBy)
    {
        Guard(businessUnitId, declaredLevel, declaredBy);

        var now = DateTime.UtcNow;
        return new BuAiDlcDeclaration(
            Guid.NewGuid(),
            businessUnitId,
            weekOf,
            declaredLevel,
            nextLevelExpectedDate,
            ragStatus,
            NullIfBlank(note),
            declaredBy,
            createdAt: now,
            updatedAt: now);
    }

    /// <summary>
    /// Resubmits the SAME week's declaration (FR-047's upsert amendment) — re-validates the same
    /// invariants as <see cref="Create"/>, bumps <see cref="UpdatedAt"/>, and reassigns
    /// <see cref="DeclaredBy"/> to whoever is resubmitting now (the declaration always reflects who
    /// most recently confirmed it, not who first created the row).
    /// </summary>
    public void Update(
        int declaredLevel,
        DateOnly? nextLevelExpectedDate,
        RagStatus ragStatus,
        string? note,
        Guid declaredBy)
    {
        Guard(BusinessUnitId, declaredLevel, declaredBy);

        DeclaredLevel = declaredLevel;
        NextLevelExpectedDate = nextLevelExpectedDate;
        RagStatus = ragStatus;
        Note = NullIfBlank(note);
        DeclaredBy = declaredBy;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void Guard(Guid businessUnitId, int declaredLevel, Guid declaredBy)
    {
        if (businessUnitId == Guid.Empty)
        {
            throw new BuReportingValidationException("A declaration requires a business unit.");
        }
        if (declaredBy == Guid.Empty)
        {
            throw new BuReportingValidationException("A declaration requires the declaring person.");
        }
        if (declaredLevel is < 0 or > 3)
        {
            throw new BuReportingValidationException($"Declared level must be between 0 and 3 (was {declaredLevel}).");
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
