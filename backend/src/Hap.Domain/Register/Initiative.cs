namespace Hap.Domain.Register;

/// <summary>
/// A registered AI initiative (data-model.md "Initiative"; FR-026 identity, FR-027 classification).
/// Register data — NOT individual assessment data — so it is not seam-guarded and carries a public
/// DbSet.
///
/// <para><b>Scope of this entity (HAP-13 + HAP-14).</b> HAP-13 shipped identity, classification,
/// create/edit authority inputs, and just enough lifecycle state for the list screen to render. HAP-14
/// adds the forward-only stage machine (<see cref="AdvanceStage"/>), the weekly RAG update
/// (<see cref="PostWeeklyUpdate"/>), and the governance/technology panels (FR-030/FR-032, informational
/// only — §4.2, no approval gate). Stage HISTORY, weekly update HISTORY, and NR lines are their own
/// entities (<see cref="InitiativeStageHistory"/>, <see cref="InitiativeWeeklyUpdate"/>,
/// <see cref="InitiativeNRLine"/>) — this entity holds only the CURRENT lifecycle state each rolls up
/// into (<see cref="CurrentStage"/>, <see cref="RagStatus"/>, <see cref="LastUpdateAt"/>).</para>
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

    // HAP-14 governance panel (FR-030 — informational only, §4.2: no approval semantics anywhere; no
    // code path reads ApprovalStatus to gate a write, see RegisterDetailEndpointsTests' explicit proof).
    public DataSensitivity DataSensitivity { get; private set; }
    public List<string> RegulatoryRelevance { get; private set; } = new();
    public string? ApprovalStatus { get; private set; }
    public string? Approver { get; private set; }
    public string? OversightModel { get; private set; }
    public string? GovernanceNotes { get; private set; }

    // HAP-14 technology panel (FR-032).
    public List<string> ModelsProviders { get; private set; } = new();
    public List<string> VendorsTools { get; private set; } = new();
    public bool UsesCogito { get; private set; }

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
        RiskTier riskTier,
        DataSensitivity dataSensitivity,
        string? approvalStatus,
        string? approver,
        string? oversightModel,
        string? governanceNotes,
        bool usesCogito)
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
        DataSensitivity = dataSensitivity;
        ApprovalStatus = approvalStatus;
        Approver = approver;
        OversightModel = oversightModel;
        GovernanceNotes = governanceNotes;
        UsesCogito = usesCogito;
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
            riskTier,
            dataSensitivity: DataSensitivity.None,
            approvalStatus: null,
            approver: null,
            oversightModel: null,
            governanceNotes: null,
            usesCogito: false);
        initiative.SetFunctions(functionsAffected);
        initiative.SetDimensions(dimensionsAdvanced);
        // RegulatoryRelevance/ModelsProviders/VendorsTools default empty (list initialisers above).
        return initiative;
    }

    /// <summary>
    /// Apply an edit (PUT). Re-validates the same required-field / range invariants. Deliberately
    /// cannot change <see cref="BusinessUnitId"/> (reassignment is out of scope) or
    /// <see cref="CurrentStage"/> (forward-only stage change is <see cref="AdvanceStage"/>'s job).
    /// The governance/technology params (HAP-14, FR-030/FR-032) re-validate NOTHING beyond what
    /// <see cref="Guard"/> already checks — governance is informational-only (§4.2), so there is no new
    /// invariant to enforce.
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
        RiskTier riskTier,
        DataSensitivity dataSensitivity = DataSensitivity.None,
        IEnumerable<string>? regulatoryRelevance = null,
        string? approvalStatus = null,
        string? approver = null,
        string? oversightModel = null,
        string? governanceNotes = null,
        IEnumerable<string>? modelsProviders = null,
        IEnumerable<string>? vendorsTools = null,
        bool usesCogito = false)
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

        DataSensitivity = dataSensitivity;
        RegulatoryRelevance = Clean(regulatoryRelevance).ToList();
        ApprovalStatus = NullIfBlank(approvalStatus);
        Approver = NullIfBlank(approver);
        OversightModel = NullIfBlank(oversightModel);
        GovernanceNotes = NullIfBlank(governanceNotes);
        ModelsProviders = Clean(modelsProviders).ToList();
        VendorsTools = Clean(vendorsTools).ToList();
        UsesCogito = usesCogito;
    }

    /// <summary>
    /// Forward-only stage transition (FR-028). The enum's DECLARATION ORDER is the forward order
    /// (Idea &lt; Evaluation &lt; Pilot &lt; Production &lt; Scaled &lt; Retired), so an ordinal
    /// comparison decides forward-vs-backward — this intentionally ALLOWS multi-step forward jumps
    /// (e.g. Idea straight to Pilot skips Evaluation entirely): "forward-only" does not mean "one step
    /// at a time". Throws <see cref="InitiativeStageTransitionException"/> when <see cref="CurrentStage"/>
    /// is already <see cref="InitiativeStage.Retired"/> (terminal — no further transition, forward or
    /// not), or when <paramref name="newStage"/> is not strictly greater than the current stage
    /// (backward or same-stage no-op). Returns the PRIOR stage on success — the caller needs it to build
    /// the <see cref="InitiativeStageHistory"/> row; this method does not write history itself (keeping
    /// persistence out of the domain model, matching this codebase's existing convention).
    /// </summary>
    public InitiativeStage AdvanceStage(InitiativeStage newStage)
    {
        if (CurrentStage == InitiativeStage.Retired)
        {
            throw new InitiativeStageTransitionException("Retired is terminal — no further stage transition is possible.");
        }
        if (newStage <= CurrentStage)
        {
            throw new InitiativeStageTransitionException(
                $"Stage transitions are forward-only: cannot move from {CurrentStage} to {newStage}.");
        }

        var priorStage = CurrentStage;
        CurrentStage = newStage;
        return priorStage;
    }

    /// <summary>Records a weekly RAG update (FR-033): refreshes <see cref="RagStatus"/> and
    /// <see cref="LastUpdateAt"/>. Does not write the <see cref="InitiativeWeeklyUpdate"/> history row
    /// itself — the caller (endpoint) does that, matching <see cref="AdvanceStage"/>'s convention.</summary>
    public void PostWeeklyUpdate(RagStatus rag, DateTime at)
    {
        RagStatus = rag;
        LastUpdateAt = at;
    }

    /// <summary>Sets (or clears) the customers-in-production figure directly, bypassing the full
    /// <see cref="Edit"/> re-validation — used by the weekly-update endpoint (FR-031/FR-033), where only
    /// this one field may change alongside the RAG/note. Category-kind gating (customer-deployed
    /// categories only) is the caller's responsibility (the entity has no category-flag reference).</summary>
    public void SetCustomersInProduction(int? value)
    {
        if (value is < 0)
        {
            throw new InitiativeValidationException("Customers in production cannot be negative.");
        }
        CustomersInProduction = value;
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
