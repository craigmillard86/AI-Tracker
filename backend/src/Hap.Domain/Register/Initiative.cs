namespace Hap.Domain.Register;

/// <summary>
/// A registered AI initiative (data-model.md "Initiative"; FR-026 identity, FR-027 classification).
/// Register data — NOT individual assessment data — so it is not seam-guarded and carries a public
/// DbSet.
///
/// <para><b>Scope of this entity (HAP-13).</b> Identity, classification, create/edit authority inputs,
/// and just enough lifecycle state for the list screen to render: <see cref="CurrentStage"/> is fixed
/// to <see cref="InitiativeStage.Idea"/> at creation (there is deliberately NO stage-change method here
/// — the forward-only transition endpoint is HAP-14), <see cref="RagStatus"/> defaults to OnTrack, and
/// <see cref="LastUpdateAt"/> starts equal to <see cref="RegisteredAt"/>. Stage history, weekly updates,
/// NR lines, and the value-capture / governance panels are later stories and are absent by design
/// (constitution Art. II.1 — no FR citation, no code).</para>
///
/// <para><see cref="DimensionsAdvanced"/> holds framework dimension KEYS (e.g. "how-ai-is-leveraged"),
/// not FKs to a version-bound Dimension row: a version-bound FK would break as the framework versions,
/// whereas the stable key survives. The API validates the keys against the current active framework
/// version's dimensions — this entity treats them as opaque strings.</para>
///
/// <para><see cref="CreatedByPersonId"/> records who called POST (a data-model.md gap-fill the AC
/// itself requires so PUT can distinguish "creator" from "owner"); <see cref="OwnerPersonId"/> is
/// FR-026's delivery-lead. The two are independent and either grants edit rights (with BU Lead of the
/// initiative's BU).</para>
/// </summary>
public sealed class Initiative
{
    public Guid Id { get; private set; }
    public Guid BusinessUnitId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? SponsorPersonId { get; private set; }
    public Guid OwnerPersonId { get; private set; }
    public Guid CreatedByPersonId { get; private set; }
    public DateTime RegisteredAt { get; private set; }
    public Guid CategoryId { get; private set; }
    public int AiDlcLevel { get; private set; }

    // Native text[] columns. Typed List<string> (EF cannot map a read-only IReadOnlyList as a
    // primitive collection) with a PRIVATE setter, mutated only through the domain methods below —
    // the private setter + gated mutation is the immutability guarantee here, and List<string> lets
    // Npgsql translate the FR-035 array-contains dimension facet to a native PostgreSQL array query.
    public List<string> FunctionsAffected { get; private set; } = new();
    public List<string> DimensionsAdvanced { get; private set; } = new();
    public InitiativeStage CurrentStage { get; private set; }
    public RagStatus RagStatus { get; private set; }
    public DateTime LastUpdateAt { get; private set; }
    public int? CustomersInProduction { get; private set; }
    public RiskTier RiskTier { get; private set; }

    // EF materialisation constructor (all columns, incl. the backing lists via the collection nav).
    private Initiative(
        Guid id,
        Guid businessUnitId,
        string name,
        string? description,
        Guid? sponsorPersonId,
        Guid ownerPersonId,
        Guid createdByPersonId,
        DateTime registeredAt,
        Guid categoryId,
        int aiDlcLevel,
        InitiativeStage currentStage,
        RagStatus ragStatus,
        DateTime lastUpdateAt,
        int? customersInProduction,
        RiskTier riskTier)
    {
        Id = id;
        BusinessUnitId = businessUnitId;
        Name = name;
        Description = description;
        SponsorPersonId = sponsorPersonId;
        OwnerPersonId = ownerPersonId;
        CreatedByPersonId = createdByPersonId;
        RegisteredAt = registeredAt;
        CategoryId = categoryId;
        AiDlcLevel = aiDlcLevel;
        CurrentStage = currentStage;
        RagStatus = ragStatus;
        LastUpdateAt = lastUpdateAt;
        CustomersInProduction = customersInProduction;
        RiskTier = riskTier;
    }

    /// <summary>
    /// Register a new initiative (FR-026/FR-027). Validates the required fields (name, BU, category,
    /// AI-DLC level, owner — AC line 27) and the AI-DLC level range (1–3), throwing
    /// <see cref="InitiativeValidationException"/> on any violation. Lifecycle state is fixed at
    /// creation: stage Idea, RAG OnTrack, last-update = registered-at.
    /// </summary>
    public static Initiative Create(
        Guid businessUnitId,
        string name,
        string? description,
        Guid? sponsorPersonId,
        Guid ownerPersonId,
        Guid createdByPersonId,
        Guid categoryId,
        int aiDlcLevel,
        IEnumerable<string>? functionsAffected,
        IEnumerable<string>? dimensionsAdvanced,
        int? customersInProduction,
        RiskTier riskTier)
    {
        Guard(businessUnitId, name, ownerPersonId, categoryId, aiDlcLevel, customersInProduction);

        var now = DateTime.UtcNow;
        var initiative = new Initiative(
            Guid.NewGuid(),
            businessUnitId,
            name.Trim(),
            NullIfBlank(description),
            sponsorPersonId,
            ownerPersonId,
            createdByPersonId,
            registeredAt: now,
            categoryId,
            aiDlcLevel,
            currentStage: InitiativeStage.Idea,
            ragStatus: RagStatus.OnTrack,
            lastUpdateAt: now,
            customersInProduction,
            riskTier);
        initiative.SetFunctions(functionsAffected);
        initiative.SetDimensions(dimensionsAdvanced);
        return initiative;
    }

    /// <summary>
    /// Apply an edit (PUT). Re-validates the same required-field / range invariants. Deliberately
    /// cannot change <see cref="BusinessUnitId"/> (reassignment is out of scope) or
    /// <see cref="CurrentStage"/> (forward-only stage change is HAP-14's endpoint).
    /// </summary>
    public void Edit(
        string name,
        string? description,
        Guid? sponsorPersonId,
        Guid ownerPersonId,
        Guid categoryId,
        int aiDlcLevel,
        IEnumerable<string>? functionsAffected,
        IEnumerable<string>? dimensionsAdvanced,
        int? customersInProduction,
        RiskTier riskTier)
    {
        Guard(BusinessUnitId, name, ownerPersonId, categoryId, aiDlcLevel, customersInProduction);

        Name = name.Trim();
        Description = NullIfBlank(description);
        SponsorPersonId = sponsorPersonId;
        OwnerPersonId = ownerPersonId;
        CategoryId = categoryId;
        AiDlcLevel = aiDlcLevel;
        CustomersInProduction = customersInProduction;
        RiskTier = riskTier;
        SetFunctions(functionsAffected);
        SetDimensions(dimensionsAdvanced);
    }

    private static void Guard(
        Guid businessUnitId, string name, Guid ownerPersonId, Guid categoryId, int aiDlcLevel, int? customersInProduction)
    {
        if (businessUnitId == Guid.Empty)
        {
            throw new InitiativeValidationException("An initiative requires a business unit.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InitiativeValidationException("An initiative requires a name.");
        }
        if (categoryId == Guid.Empty)
        {
            throw new InitiativeValidationException("An initiative requires a Harris category.");
        }
        if (ownerPersonId == Guid.Empty)
        {
            throw new InitiativeValidationException("An initiative requires an owner.");
        }
        if (aiDlcLevel is < 1 or > 3)
        {
            throw new InitiativeValidationException($"AI-DLC level must be between 1 and 3 (was {aiDlcLevel}).");
        }
        if (customersInProduction is < 0)
        {
            throw new InitiativeValidationException("Customers in production cannot be negative.");
        }
    }

    private void SetFunctions(IEnumerable<string>? functions) =>
        FunctionsAffected = Clean(functions).ToList();

    private void SetDimensions(IEnumerable<string>? dimensions) =>
        DimensionsAdvanced = Clean(dimensions).ToList();

    private static IEnumerable<string> Clean(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.Ordinal);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
