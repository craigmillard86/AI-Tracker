namespace Hap.Domain.Register;

/// <summary>
/// One NR (net revenue) capture line for an initiative (data-model.md "InitiativeNRLine"; FR-029/FR-045).
/// Unlike <see cref="InitiativeStageHistory"/>/<see cref="InitiativeWeeklyUpdate"/> this entity is NOT
/// append-only: a line can be deleted while unreferenced by a persisted Harris submission (contracts/api.md
/// "Register" — "editable until referenced by a persisted monthly submission, then 409"). It mirrors
/// <see cref="Initiative"/>'s own style instead: private setters mutated only through domain methods, a
/// static <see cref="Create"/> factory that validates.
///
/// <para><see cref="ReferencedBySubmissionLineId"/> and <see cref="MarkReferencedBySubmission"/> are a
/// HAP-16 stub: no <c>HarrisSubmission</c>/<c>HarrisSubmissionLine</c> table exists yet in this build
/// (data-model.md "Harris reporting" — HAP-16 scope). This story only needs the LOCKED state to exist so
/// the delete-guard's 409 path is real; HAP-16 will be the first caller that ever invokes
/// <see cref="MarkReferencedBySubmission"/> from a genuine submission-generation code path. Until then,
/// tests call it directly (or force the column via raw SQL, mirroring HAP-13's own
/// <c>Facet_stage_filters</c> test) to simulate the locked state.</para>
/// </summary>
public sealed class InitiativeNRLine
{
    public Guid Id { get; private set; }
    public Guid InitiativeId { get; private set; }
    public int Year { get; private set; }
    public NRDirection Direction { get; private set; }
    public NRRecurrence Recurrence { get; private set; }
    public decimal AmountUsd { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Non-null once a persisted Harris submission line has referenced this NR line — from
    /// that point the delete endpoint 409s (reconciliation integrity, FR-046). Null (unreferenced) at
    /// creation and until <see cref="MarkReferencedBySubmission"/> is called.</summary>
    public Guid? ReferencedBySubmissionLineId { get; private set; }

    // EF materialisation constructor.
    private InitiativeNRLine(
        Guid id,
        Guid initiativeId,
        int year,
        NRDirection direction,
        NRRecurrence recurrence,
        decimal amountUsd,
        string? description,
        Guid? referencedBySubmissionLineId)
    {
        Id = id;
        InitiativeId = initiativeId;
        Year = year;
        Direction = direction;
        Recurrence = recurrence;
        AmountUsd = amountUsd;
        Description = description;
        ReferencedBySubmissionLineId = referencedBySubmissionLineId;
    }

    /// <summary>Adds a new NR line (FR-029). Validates year is in a plausible range (2000–2100) and
    /// amount is non-negative, throwing <see cref="InitiativeValidationException"/> on violation.</summary>
    public static InitiativeNRLine Create(
        Guid initiativeId,
        int year,
        NRDirection direction,
        NRRecurrence recurrence,
        decimal amountUsd,
        string? description)
    {
        if (year is < 2000 or > 2100)
        {
            throw new InitiativeValidationException($"NR line year must be between 2000 and 2100 (was {year}).");
        }
        if (amountUsd < 0)
        {
            throw new InitiativeValidationException("NR line amount cannot be negative.");
        }

        return new InitiativeNRLine(
            Guid.NewGuid(),
            initiativeId,
            year,
            direction,
            recurrence,
            amountUsd,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            referencedBySubmissionLineId: null);
    }

    /// <summary>HAP-16 stub: records that a persisted Harris submission line now references this NR
    /// line, locking it against deletion (see class doc). No submission table exists yet — this method
    /// exists so this story's delete-guard has a real locked state to defend; HAP-16's submission
    /// generation is the eventual real caller.</summary>
    public void MarkReferencedBySubmission(Guid submissionLineId) =>
        ReferencedBySubmissionLineId = submissionLineId;
}
