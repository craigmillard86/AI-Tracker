namespace Hap.Domain.Reporting;

/// <summary>
/// The internal-support-savings panel of a <see cref="BuMonthlyMetrics"/> row (data-model.md
/// "BUMonthlyMetrics" <c>support_internal</c> jsonb; FR-048). Owned type — mapped to a native jsonb
/// column via EF Core's <c>OwnsOne(...).ToJson()</c> (<c>BuMonthlyMetricsConfiguration</c>), never
/// shredded into separate relational columns.
///
/// <para><b>Current-month only (FR-048), by design — no fields here ever carry forward.</b> Unlike
/// <see cref="SupportCustomer"/>'s YTD figures, this panel (and <see cref="BuMonthlyMetrics.SorCalledByOtherApps"/>)
/// starts blank every month regardless of what the prior month held: "Per Harris form instructions, SOR
/// usage is current-month, not YTD" (mockup help text) — the same current-month rule applies here. The
/// endpoint layer enforces this by never reading a prior month's <see cref="SupportInternal"/> when
/// pre-filling a new month's GET.</para>
/// </summary>
public sealed class SupportInternal
{
    public decimal? TimeSavingsPct { get; private set; }
    public string? FewerPeopleNeeded { get; private set; }
    public string? SupportRatioImpact { get; private set; }

    public SupportInternal(decimal? timeSavingsPct, string? fewerPeopleNeeded, string? supportRatioImpact)
    {
        TimeSavingsPct = timeSavingsPct;
        FewerPeopleNeeded = string.IsNullOrWhiteSpace(fewerPeopleNeeded) ? null : fewerPeopleNeeded.Trim();
        SupportRatioImpact = string.IsNullOrWhiteSpace(supportRatioImpact) ? null : supportRatioImpact.Trim();
    }

    /// <summary>An all-blank panel — the default for a month with no submitted internal-support figures
    /// yet (never pre-filled from a prior month; see class doc).</summary>
    public static readonly SupportInternal Empty = new(null, null, null);
}

/// <summary>
/// The customer-support YTD panel of a <see cref="BuMonthlyMetrics"/> row (data-model.md
/// "BUMonthlyMetrics" <c>support_customer</c> jsonb; FR-048). Owned type, same jsonb mapping as
/// <see cref="SupportInternal"/>.
///
/// <para><b>These four fields are the ONLY ones FR-048's YTD carry-forward applies to.</b> The entity
/// itself has no carry-forward behaviour (a pure data holder, like <see cref="SupportInternal"/>) — the
/// endpoint layer's GET pre-fill is what copies a prior month's <see cref="SupportCustomer"/> onto a new
/// month's response when no row yet exists for the requested month.</para>
/// </summary>
public sealed class SupportCustomer
{
    public int? CustomersYtd { get; private set; }
    public int? TicketsYtd { get; private set; }
    public int? ResolvedByAiYtd { get; private set; }
    public int? AiAssistedYtd { get; private set; }

    public SupportCustomer(int? customersYtd, int? ticketsYtd, int? resolvedByAiYtd, int? aiAssistedYtd)
    {
        CustomersYtd = customersYtd;
        TicketsYtd = ticketsYtd;
        ResolvedByAiYtd = resolvedByAiYtd;
        AiAssistedYtd = aiAssistedYtd;
    }

    /// <summary>An all-blank panel — the default for a month with no prior-month figures to carry
    /// forward and none submitted yet.</summary>
    public static readonly SupportCustomer Empty = new(null, null, null, null);
}

/// <summary>
/// A BU's monthly Support/SOR metrics submission (data-model.md "BUMonthlyMetrics"; FR-048). One row
/// per BU per calendar month — <see cref="Month"/> is always the first of the month, normalised at the
/// endpoint layer exactly like <see cref="BuAiDlcDeclaration.WeekOf"/> (this entity has no calendar
/// knowledge either).
///
/// <para><b>Upsert, not append</b> — same shape as <see cref="BuAiDlcDeclaration"/>: <see cref="Update"/>
/// is the same-month resubmission path, guarded at the DB by the unique index on (BusinessUnitId,
/// Month) (<c>BuMonthlyMetricsConfiguration</c>).</para>
/// </summary>
public sealed class BuMonthlyMetrics
{
    public Guid Id { get; private set; }
    public Guid BusinessUnitId { get; private set; }
    public DateOnly Month { get; private set; }

    // Owned-type navigations (mapped to jsonb via ToJson()) — EF Core cannot constructor-bind an owned
    // navigation (only scalar/primitive-collection properties), so these are NOT parameters of the
    // materialisation constructor below; EF sets them via the private setter after construction, same
    // as this class's own Create/Update do. Field initialisers give every code path (both EF
    // materialisation and the parameterless-of-owned-figures constructor call in Create) a non-null
    // starting value.
    public SupportInternal SupportInternal { get; private set; } = SupportInternal.Empty;
    public SupportCustomer SupportCustomer { get; private set; } = SupportCustomer.Empty;

    /// <summary>Current-month-only (FR-048) — never carried forward; see <see cref="SupportInternal"/>'s
    /// class doc for the same rule applied to that panel.</summary>
    public string? SorCalledByOtherApps { get; private set; }

    public Guid SubmittedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // EF materialisation constructor for the SCALAR properties only (parameter names match property
    // names by convention). SupportInternal/SupportCustomer are deliberately absent — see the field
    // initialiser comment above.
    private BuMonthlyMetrics(
        Guid id,
        Guid businessUnitId,
        DateOnly month,
        string? sorCalledByOtherApps,
        Guid submittedBy,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        BusinessUnitId = businessUnitId;
        Month = month;
        SorCalledByOtherApps = sorCalledByOtherApps;
        SubmittedBy = submittedBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Records a BU's first metrics submission for a given (already first-of-month-normalised) month.
    /// A null <paramref name="supportInternal"/>/<paramref name="supportCustomer"/> is normalised to
    /// <see cref="SupportInternal.Empty"/>/<see cref="SupportCustomer.Empty"/>. Validates the numeric
    /// invariants (see <see cref="Guard"/>), throwing <see cref="BuReportingValidationException"/> on
    /// violation.
    /// </summary>
    public static BuMonthlyMetrics Create(
        Guid businessUnitId,
        DateOnly month,
        SupportInternal? supportInternal,
        SupportCustomer? supportCustomer,
        string? sorCalledByOtherApps,
        Guid submittedBy)
    {
        var internalFigures = supportInternal ?? SupportInternal.Empty;
        var customerFigures = supportCustomer ?? SupportCustomer.Empty;
        Guard(businessUnitId, submittedBy, internalFigures, customerFigures);

        var now = DateTime.UtcNow;
        var entity = new BuMonthlyMetrics(
            Guid.NewGuid(),
            businessUnitId,
            month,
            NullIfBlank(sorCalledByOtherApps),
            submittedBy,
            createdAt: now,
            updatedAt: now);
        entity.SupportInternal = internalFigures;
        entity.SupportCustomer = customerFigures;
        return entity;
    }

    /// <summary>Resubmits the SAME month's metrics — same upsert shape as
    /// <see cref="BuAiDlcDeclaration.Update"/>: re-validates, bumps <see cref="UpdatedAt"/>, reassigns
    /// <see cref="SubmittedBy"/> to whoever is resubmitting now.</summary>
    public void Update(
        SupportInternal? supportInternal,
        SupportCustomer? supportCustomer,
        string? sorCalledByOtherApps,
        Guid submittedBy)
    {
        var internalFigures = supportInternal ?? SupportInternal.Empty;
        var customerFigures = supportCustomer ?? SupportCustomer.Empty;
        Guard(BusinessUnitId, submittedBy, internalFigures, customerFigures);

        SupportInternal = internalFigures;
        SupportCustomer = customerFigures;
        SorCalledByOtherApps = NullIfBlank(sorCalledByOtherApps);
        SubmittedBy = submittedBy;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void Guard(
        Guid businessUnitId, Guid submittedBy, SupportInternal internalFigures, SupportCustomer customerFigures)
    {
        if (businessUnitId == Guid.Empty)
        {
            throw new BuReportingValidationException("Monthly metrics require a business unit.");
        }
        if (submittedBy == Guid.Empty)
        {
            throw new BuReportingValidationException("Monthly metrics require the submitting person.");
        }
        if (internalFigures.TimeSavingsPct is < 0 or > 100)
        {
            throw new BuReportingValidationException("Time savings percentage must be between 0 and 100.");
        }
        if (customerFigures.CustomersYtd is < 0)
        {
            throw new BuReportingValidationException("Customers YTD cannot be negative.");
        }
        if (customerFigures.TicketsYtd is < 0)
        {
            throw new BuReportingValidationException("Tickets YTD cannot be negative.");
        }
        if (customerFigures.ResolvedByAiYtd is < 0)
        {
            throw new BuReportingValidationException("Resolved-by-AI YTD cannot be negative.");
        }
        if (customerFigures.AiAssistedYtd is < 0)
        {
            throw new BuReportingValidationException("AI-assisted YTD cannot be negative.");
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
