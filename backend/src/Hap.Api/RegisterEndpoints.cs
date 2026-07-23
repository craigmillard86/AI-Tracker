using System.Security.Claims;
using Hap.Api.Identity;
using Hap.Domain.Frameworks;
using Hap.Domain.Org;
using Hap.Domain.Register;
using Hap.Infrastructure;
using Hap.Infrastructure.Register;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api;

/// <summary>
/// Initiative-register surfaces (contracts/api.md "Register"; FR-026 identity, FR-027 classification,
/// FR-034 create/edit authority, FR-035 search + facets). The register is NOT visibility-seam-gated —
/// it holds initiative data, not individual assessment data — so every authenticated role may READ it
/// (<c>GET</c>); WRITE authority (<c>POST</c>/<c>PUT</c>) is bounded per FR-034 (amended 2026-07-21):
/// Managers and BU Leads only, within their own BU; roles above BU level are read-only.
///
/// <para><b>Create authority (FR-034), role-precedence — the load-bearing order.</b> A Group Leader is
/// ALSO structurally <c>IsManager=true</c> (they have direct reports who are BU Leads), so the leadership
/// anchors MUST be checked BEFORE the bare <c>IsManager</c> fallback, or Group/Portfolio Leaders would
/// wrongly gain create rights. The order (mirroring <c>RollupReads</c>'s scope precedence) is: explicit
/// HIG Executive grant → deny (above-BU); Portfolio Leader anchor → deny; Group Leader anchor → deny; BU
/// Lead anchor → allow, scoped to that BU; else plain Manager (IsManager, no anchor) → allow, scoped to
/// their own home BU; else Individual → deny. See <see cref="ResolveWritableBusinessUnitAsync"/>.</para>
///
/// <para><b>Status codes.</b> POST uses 403 for "your role/BU doesn't permit creating here" — there is
/// no addressed resource whose existence could leak, and the caller already knows their own role. PUT
/// uses 404 for a caller who is neither owner, creator, nor BU Lead of the initiative's BU (the
/// existence-leak convention matching <c>TeamEndpoints</c>). Validation failures are 422.</para>
/// </summary>
public static class RegisterEndpoints
{
    public static void MapRegisterEndpoints(this RouteGroupBuilder api)
    {
        // GET /api/initiatives — full-text search + facets (FR-035). Every authenticated role may read.
        api.MapGet("/initiatives", async (
            HttpContext http,
            HapDbContext db,
            string? search,
            Guid? bu,
            Guid? category,
            string? stage,
            string? riskTier,
            int? aiDlcLevel,
            string? dimension,
            CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out _))
            {
                return MissingPrincipal();
            }

            var query = db.Initiatives.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                query = query.Where(i =>
                    EF.Functions.ILike(i.Name, term)
                    || (i.Description != null && EF.Functions.ILike(i.Description, term)));
            }
            if (bu is Guid buId)
            {
                query = query.Where(i => i.BusinessUnitId == buId);
            }
            if (category is Guid categoryId)
            {
                query = query.Where(i => i.CategoryId == categoryId);
            }
            if (!string.IsNullOrWhiteSpace(stage) && Enum.TryParse<InitiativeStage>(stage, ignoreCase: true, out var stageValue))
            {
                query = query.Where(i => i.CurrentStage == stageValue);
            }
            if (!string.IsNullOrWhiteSpace(riskTier) && Enum.TryParse<RiskTier>(riskTier, ignoreCase: true, out var riskValue))
            {
                query = query.Where(i => i.RiskTier == riskValue);
            }
            if (aiDlcLevel is int level)
            {
                query = query.Where(i => i.AiDlcLevel == level);
            }
            if (!string.IsNullOrWhiteSpace(dimension))
            {
                var dim = dimension.Trim();
                query = query.Where(i => i.DimensionsAdvanced.Contains(dim));
            }

            var initiatives = await query
                .OrderByDescending(i => i.LastUpdateAt)
                .ToListAsync(ct);

            var stageMap = await StageMapAsync(db, ct);
            return Results.Ok(initiatives.Select(i => InitiativeResponse.From(i, stageMap)).ToList());
        });

        // GET /api/initiatives/{id} — the full initiative incl. stage history, NR lines, and update
        // trail (FR-026; HAP-14 extends this to InitiativeDetailResponse — contracts/api.md "Register").
        // Readable by any authenticated role (register data, not seam-gated); CanEdit is computed
        // server-side via the SAME logic PUT enforces, so the UI can gate write controls without
        // duplicating auth logic client-side.
        api.MapGet("/initiatives/{id:guid}", async (
            Guid id, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var stageMap = await StageMapAsync(db, ct);
            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            // SingleOrDefaultAsync, not SingleAsync (panel finding, HAP-14): the initiative's
            // CategoryId is not an enforced FK to HarrisCategories (see InitiativeConfiguration's
            // comment on why owner/sponsor/creator go unenforced — category itself IS an FK here, but
            // this guards the same class of "referenced row absent" surprise with a diagnostic 500
            // instead of SingleAsync's opaque InvalidOperationException).
            var categoryCustomerDeployedRaw = await db.HarrisCategories.AsNoTracking()
                .Where(c => c.Id == initiative.CategoryId)
                .Select(c => (bool?)c.CustomerDeployed)
                .SingleOrDefaultAsync(ct);
            if (categoryCustomerDeployedRaw is not bool categoryCustomerDeployed)
            {
                return Results.Problem(
                    $"Initiative {id} references Harris category {initiative.CategoryId}, which no longer exists.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var stageHistory = await db.InitiativeStageHistories.AsNoTracking()
                .Where(h => h.InitiativeId == id)
                .OrderBy(h => h.EnteredAt)
                .Select(h => new StageHistoryEntryResponse(
                    h.Id, h.Stage.ToString(), h.PriorStage == null ? null : h.PriorStage.ToString(), h.EnteredAt, h.EnteredBy))
                .ToListAsync(ct);

            var nrLines = await db.InitiativeNRLines.AsNoTracking()
                .Where(l => l.InitiativeId == id)
                .OrderBy(l => l.Year)
                .Select(l => NrLineResponse.From(l))
                .ToListAsync(ct);

            // Newest-first per the AC ("update trail returned newest-first on the detail endpoint").
            var updates = await db.InitiativeWeeklyUpdates.AsNoTracking()
                .Where(u => u.InitiativeId == id)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => WeeklyUpdateResponse.From(u))
                .ToListAsync(ct);

            return Results.Ok(InitiativeDetailResponse.From(
                initiative, stageMap, canEdit, categoryCustomerDeployed, stageHistory, nrLines, updates));
        });

        // POST /api/initiatives — Manager/BU Lead within own BU only (FR-034).
        api.MapPost("/initiatives", async (
            CreateInitiativeRequest request, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var writableBu = await ResolveWritableBusinessUnitAsync(callerId, db, hierarchy, ct);
            if (writableBu is null)
            {
                // Caller's role does not permit creating any initiative (above-BU role, or Individual).
                return Results.Forbid();
            }
            if (request.BusinessUnitId != writableBu.Value)
            {
                // Caller may create only in their own BU — a different BU is out of their write scope.
                return Results.Forbid();
            }

            // Fetched as an entity (not AnyAsync) so CustomerDeployed drives the FR-031 gating below.
            var category = await db.HarrisCategories.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct);
            if (category is null)
            {
                return Results.UnprocessableEntity(Problem("category", "Unknown Harris category."));
            }

            var dimensionError = await ValidateDimensionKeysAsync(db, request.DimensionsAdvanced, ct);
            if (dimensionError is not null)
            {
                return Results.UnprocessableEntity(Problem("dimensionsAdvanced", dimensionError));
            }

            if (!TryParseRiskTier(request.RiskTier, out var riskTier))
            {
                return Results.UnprocessableEntity(
                    Problem("riskTier", $"Unrecognised risk tier '{request.RiskTier}'."));
            }

            try
            {
                var initiative = Initiative.Create(
                    request.BusinessUnitId,
                    request.Name,
                    request.Description,
                    request.SponsorPersonId,
                    request.OwnerPersonId,
                    createdByPersonId: callerId,
                    request.CategoryId,
                    request.AiDlcLevel,
                    request.FunctionsAffected,
                    request.DimensionsAdvanced,
                    // FR-031: customers-in-production is only meaningful for customer-deployed
                    // categories — a submitted value against a non-customer-deployed category is
                    // silently normalised to null (data-integrity, not a validation failure).
                    category.CustomerDeployed ? request.CustomersInProduction : null,
                    riskTier);

                db.Initiatives.Add(initiative);
                // HAP-14: every initiative's stage trail is complete from birth — the initial Idea row
                // has no PriorStage (there is no "before").
                db.InitiativeStageHistories.Add(
                    InitiativeStageHistory.Create(initiative.Id, InitiativeStage.Idea, priorStage: null, callerId));
                await db.SaveChangesAsync(ct);

                var stageMap = await StageMapAsync(db, ct);
                return Results.Created($"/api/initiatives/{initiative.Id}", InitiativeResponse.From(initiative, stageMap));
            }
            catch (InitiativeValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("initiative", ex.Message));
            }
        });

        // PUT /api/initiatives/{id} — owner, creator, or BU Lead of that BU (FR-034).
        api.MapPut("/initiatives/{id:guid}", async (
            Guid id, UpdateInitiativeRequest request, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            if (!canEdit)
            {
                // Neither owner, creator, nor BU Lead of the initiative's BU — 404 (existence-leak).
                return Results.NotFound();
            }

            // Fetched as an entity (not AnyAsync) so CustomerDeployed drives the FR-031 gating below.
            var category = await db.HarrisCategories.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == request.CategoryId, ct);
            if (category is null)
            {
                return Results.UnprocessableEntity(Problem("category", "Unknown Harris category."));
            }

            var dimensionError = await ValidateDimensionKeysAsync(db, request.DimensionsAdvanced, ct);
            if (dimensionError is not null)
            {
                return Results.UnprocessableEntity(Problem("dimensionsAdvanced", dimensionError));
            }

            if (!TryParseRiskTier(request.RiskTier, out var riskTier))
            {
                return Results.UnprocessableEntity(
                    Problem("riskTier", $"Unrecognised risk tier '{request.RiskTier}'."));
            }

            if (!TryParseDataSensitivity(request.DataSensitivity, out var dataSensitivity))
            {
                return Results.UnprocessableEntity(
                    Problem("dataSensitivity", $"Unrecognised data sensitivity '{request.DataSensitivity}'."));
            }

            try
            {
                initiative.Edit(
                    request.Name,
                    request.Description,
                    request.SponsorPersonId,
                    request.OwnerPersonId,
                    request.CategoryId,
                    request.AiDlcLevel,
                    request.FunctionsAffected,
                    request.DimensionsAdvanced,
                    // FR-031: same normalisation as POST — never persist a customer count against a
                    // non-customer-deployed category, regardless of what was submitted.
                    category.CustomerDeployed ? request.CustomersInProduction : null,
                    riskTier,
                    dataSensitivity,
                    request.RegulatoryRelevance,
                    request.ApprovalStatus,
                    request.Approver,
                    request.OversightModel,
                    request.GovernanceNotes,
                    request.ModelsProviders,
                    request.VendorsTools,
                    request.UsesCogito);

                await db.SaveChangesAsync(ct);

                var stageMap = await StageMapAsync(db, ct);
                return Results.Ok(InitiativeResponse.From(initiative, stageMap));
            }
            catch (InitiativeValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("initiative", ex.Message));
            }
        });

        // POST /api/initiatives/{id}/stage — forward-only transition (FR-028); same edit permission as
        // PUT (404 existence-leak convention); 409 on backward/no-op/Retired-is-terminal.
        api.MapPost("/initiatives/{id:guid}/stage", async (
            Guid id, StageChangeRequest request, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            if (!canEdit)
            {
                return Results.NotFound();
            }

            if (!TryParseStage(request.Stage, out var target))
            {
                return Results.UnprocessableEntity(Problem("stage", $"Unrecognised stage '{request.Stage}'."));
            }

            try
            {
                var priorStage = initiative.AdvanceStage(target);
                db.InitiativeStageHistories.Add(
                    InitiativeStageHistory.Create(initiative.Id, target, priorStage, callerId));
                await db.SaveChangesAsync(ct);

                var stageMap = await StageMapAsync(db, ct);
                return Results.Ok(InitiativeResponse.From(initiative, stageMap));
            }
            catch (InitiativeStageTransitionException)
            {
                return Results.Conflict();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another transition on this initiative committed first (xmin moved) between our read
                // and our write — the loser's save rolls back and gets the same 409 semantics as any
                // other stage-transition conflict (panel finding, HAP-14; see InitiativeConfiguration's
                // xmin comment).
                return Results.Conflict();
            }
        });

        // POST /api/initiatives/{id}/updates — weekly RAG + note (FR-033); same edit permission as PUT.
        api.MapPost("/initiatives/{id:guid}/updates", async (
            Guid id, PostWeeklyUpdateRequest request, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            if (!canEdit)
            {
                return Results.NotFound();
            }

            if (!TryParseRagStatus(request.RagStatus, out var rag))
            {
                return Results.UnprocessableEntity(Problem("ragStatus", $"Unrecognised RAG status '{request.RagStatus}'."));
            }

            try
            {
                // FR-031 gating (same rule as POST/PUT): a submitted customer count only applies for
                // customer-deployed categories — otherwise it is silently ignored (not 422; mirrors how
                // Edit already treats this field permissively).
                if (request.CustomersInProduction is not null)
                {
                    var categoryDeployed = await db.HarrisCategories.AsNoTracking()
                        .Where(c => c.Id == initiative.CategoryId)
                        .Select(c => c.CustomerDeployed)
                        .SingleAsync(ct);
                    if (categoryDeployed)
                    {
                        initiative.SetCustomersInProduction(request.CustomersInProduction);
                    }
                }

                // Single instant for both writes (panel finding, HAP-14): LastUpdateAt and the row's
                // CreatedAt must agree — they record the same logical event.
                var now = DateTime.UtcNow;
                initiative.PostWeeklyUpdate(rag, now);
                var update = InitiativeWeeklyUpdate.Create(initiative.Id, rag, request.Note, callerId, now);
                db.InitiativeWeeklyUpdates.Add(update);
                await db.SaveChangesAsync(ct);

                return Results.Created(
                    $"/api/initiatives/{initiative.Id}", WeeklyUpdateResponse.From(update));
            }
            catch (InitiativeValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("customersInProduction", ex.Message));
            }
        });

        // POST /api/initiatives/{id}/nr-lines — add an NR capture line (FR-029); same edit permission.
        api.MapPost("/initiatives/{id:guid}/nr-lines", async (
            Guid id, CreateNrLineRequest request, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            if (!canEdit)
            {
                return Results.NotFound();
            }

            if (!TryParseNRDirection(request.Direction, out var direction))
            {
                return Results.UnprocessableEntity(Problem("direction", $"Unrecognised NR direction '{request.Direction}'."));
            }
            if (!TryParseNRRecurrence(request.Recurrence, out var recurrence))
            {
                return Results.UnprocessableEntity(Problem("recurrence", $"Unrecognised NR recurrence '{request.Recurrence}'."));
            }

            try
            {
                var line = InitiativeNRLine.Create(
                    id, request.Year, direction, recurrence, request.AmountUsd, request.Description);
                db.InitiativeNRLines.Add(line);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/initiatives/{id}/nr-lines/{line.Id}", NrLineResponse.From(line));
            }
            catch (InitiativeValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("nrLine", ex.Message));
            }
        });

        // DELETE /api/initiatives/{id}/nr-lines/{lineId} — editable until referenced by a persisted
        // Harris submission line, then 409 (reconciliation integrity; HAP-16 stub — see
        // InitiativeNRLine.MarkReferencedBySubmission). Same edit permission as PUT.
        api.MapDelete("/initiatives/{id:guid}/nr-lines/{lineId:guid}", async (
            Guid id, Guid lineId, HttpContext http, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var canEdit = await CanEditAsync(callerId, initiative, db, hierarchy, ct);
            if (!canEdit)
            {
                return Results.NotFound();
            }

            // Not found, OR belongs to a different initiative — both 404 (no existence leak across
            // initiatives, matching the class-wide convention).
            var line = await db.InitiativeNRLines.SingleOrDefaultAsync(l => l.Id == lineId && l.InitiativeId == id, ct);
            if (line is null)
            {
                return Results.NotFound();
            }

            if (line.ReferencedBySubmissionLineId is not null)
            {
                return Results.Conflict();
            }

            db.InitiativeNRLines.Remove(line);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // GET /api/harris-categories — reference data for the register filters + create/edit form
        // (FR-027). Every authenticated role may read (same non-seam-gated rule as /initiatives).
        api.MapGet("/harris-categories", async (HttpContext http, HapDbContext db, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out _))
            {
                return MissingPrincipal();
            }

            var categories = await db.HarrisCategories.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new HarrisCategoryResponse(c.Id, c.Key, c.Name, c.GroupReported, c.CustomerDeployed))
                .ToListAsync(ct);
            return Results.Ok(categories);
        });

        // GET /api/business-units — reference data for the register filters + create/edit form. Not
        // register-specific data but the smallest surface that unblocks the filters panel; a fuller org
        // directory endpoint (if ever needed elsewhere) is a separate story's concern.
        api.MapGet("/business-units", async (HttpContext http, HapDbContext db, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out _))
            {
                return MissingPrincipal();
            }

            var units = await db.BusinessUnits.AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new BusinessUnitResponse(b.Id, b.Code, b.Name))
                .ToListAsync(ct);
            return Results.Ok(units);
        });

        // POST /api/admin/harris-taxonomy — Platform Admin seed of the Harris taxonomy (FR-027/FR-064).
        // Explicit, idempotent, re-runnable import (mirrors POST /api/admin/frameworks for the framework
        // definition, HAP-6). Register write authority above (FR-034) is unrelated to this admin path.
        api.MapPost("/admin/harris-taxonomy", async (HarrisTaxonomySeeder seeder, CancellationToken ct) =>
        {
            var result = await seeder.SeedAsync(ct);
            return Results.Ok(result);
        }).RequireAuthorization("PlatformAdmin");
    }

    /// <summary>
    /// The single BU the caller may create/curate initiatives in, or null if their role permits none.
    /// Implements FR-034's create authority with the load-bearing role precedence (see class doc): the
    /// leadership anchors are checked BEFORE the bare <c>IsManager</c> fallback so a Group/Portfolio
    /// Leader (who is structurally also a manager) is correctly denied.
    /// </summary>
    private static async Task<Guid?> ResolveWritableBusinessUnitAsync(
        Guid callerId, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct)
    {
        // Explicit HIG Executive grant → above-BU, read-only.
        var hasExecutiveGrant = await db.RoleGrants.AnyAsync(g => g.PersonId == callerId && g.Role == OrgRole.HigExecutive, ct);
        if (hasExecutiveGrant)
        {
            return null;
        }

        var roles = await hierarchy.ResolveAsync(callerId, ct);

        // Above-BU hierarchy leaders are read-only (checked before the bare IsManager fallback).
        if (roles.PortfolioLeaderOfPortfolioId is not null)
        {
            return null;
        }
        if (roles.GroupLeaderOfGroupId is not null)
        {
            return null;
        }
        // BU Lead → curates their own BU (any entry in it).
        if (roles.BuLeadOfBusinessUnitId is Guid buLeadBu)
        {
            return buLeadBu;
        }
        // Plain manager (has reports, no leadership anchor) → their own home BU only.
        if (roles.IsManager)
        {
            var homeBu = await db.People
                .Where(p => p.Id == callerId)
                .Select(p => (Guid?)p.BusinessUnitId)
                .SingleOrDefaultAsync(ct);
            return homeBu;
        }

        // Individual with no reports → no create authority.
        return null;
    }

    /// <summary>PUT permission (FR-034): owner, creator, or BU Lead of the initiative's own BU.</summary>
    private static async Task<bool> CanEditAsync(
        Guid callerId, Initiative initiative, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct)
    {
        if (initiative.OwnerPersonId == callerId || initiative.CreatedByPersonId == callerId)
        {
            return true;
        }

        var roles = await hierarchy.ResolveAsync(callerId, ct);
        return roles.BuLeadOfBusinessUnitId is Guid buLeadBu && buLeadBu == initiative.BusinessUnitId;
    }

    /// <summary>Validates every submitted dimension key against the CURRENT active framework version's
    /// dimension keys (not a DB FK — keys are version-stable, a specific Dimension row is not). Returns an
    /// error message naming the unknown keys, or null when all keys are valid (or none were submitted).</summary>
    private static async Task<string?> ValidateDimensionKeysAsync(
        HapDbContext db, IReadOnlyList<string>? submitted, CancellationToken ct)
    {
        var keys = (submitted ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0)
        {
            return null;
        }

        var activeVersion = await db.FrameworkVersions
            .Where(v => v.Status == FrameworkVersionStatus.Active)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);
        if (activeVersion is null)
        {
            return "No active framework version to validate dimensions against.";
        }

        var validKeys = (await db.Dimensions
                .Where(d => d.FrameworkVersionId == activeVersion.Value)
                .Select(d => d.Key)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = keys.Where(k => !validKeys.Contains(k)).ToList();
        return unknown.Count == 0
            ? null
            : $"Unknown framework dimension key(s): {string.Join(", ", unknown)}.";
    }

    private static async Task<IReadOnlyDictionary<InitiativeStage, HarrisStage>> StageMapAsync(
        HapDbContext db, CancellationToken ct) =>
        await db.HarrisStageMaps.AsNoTracking()
            .ToDictionaryAsync(s => s.InternalStage, s => s.HarrisStage, ct);

    /// <summary>Parses <see cref="RiskTier"/> from the wire (FR-030 governance field). Null/blank
    /// defaults to <see cref="RiskTier.Low"/> (an omitted risk tier is a valid, low-risk default); a
    /// non-null value that doesn't match a known tier fails so the caller can 422 rather than silently
    /// coercing an unrecognised value to Low. The <see cref="Enum.IsDefined(Type,object)"/> check (QA
    /// finding, HAP-13) is load-bearing: <c>Enum.TryParse</c> alone succeeds for ANY value convertible to
    /// the underlying integral type, not just declared members — so a bare TryParse let a numeric string
    /// like "99" through as an undefined <see cref="RiskTier"/> that then persisted intact (the column is
    /// <c>HasConversion&lt;string&gt;()</c>, so "99" round-tripped as-is). A named-but-unrecognised value
    /// like "Medium" was already caught by TryParse failing outright; this closes the numeric-string gap
    /// alongside it.</summary>
    private static bool TryParseRiskTier(string? raw, out RiskTier value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = RiskTier.Low;
            return true;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>Parses <see cref="DataSensitivity"/> from the wire (FR-030 governance field). Same
    /// null/blank-defaults-to-lowest-value convention as <see cref="TryParseRiskTier"/> (an omitted
    /// sensitivity is a valid "None" default), with the same <see cref="Enum.IsDefined(Type,object)"/>
    /// belt-and-braces check against the numeric-string gap.</summary>
    private static bool TryParseDataSensitivity(string? raw, out DataSensitivity value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = DataSensitivity.None;
            return true;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>Parses <see cref="InitiativeStage"/> from the wire (FR-028 stage-transition target).
    /// Unlike <see cref="TryParseRiskTier"/>/<see cref="TryParseDataSensitivity"/> there is no valid
    /// default for an omitted stage — a stage-change request MUST name a target stage, so blank fails
    /// just like an unrecognised value.</summary>
    private static bool TryParseStage(string? raw, out InitiativeStage value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>Parses <see cref="RagStatus"/> from the wire (FR-033 weekly update). No valid default
    /// for an omitted value — every weekly update names an explicit RAG status.</summary>
    private static bool TryParseRagStatus(string? raw, out RagStatus value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>Parses <see cref="NRDirection"/> from the wire (FR-029 NR line). No valid default.</summary>
    private static bool TryParseNRDirection(string? raw, out NRDirection value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    /// <summary>Parses <see cref="NRRecurrence"/> from the wire (FR-029 NR line). No valid default.</summary>
    private static bool TryParseNRRecurrence(string? raw, out NRRecurrence value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }
        return Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static bool TryGetPersonId(HttpContext http, out Guid personId) =>
        Guid.TryParse(http.User.FindFirstValue("person_id"), out personId);

    private static IResult MissingPrincipal() =>
        Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);

    private static Dictionary<string, string[]> Problem(string field, string message) =>
        new() { [field] = new[] { message } };
}

/// <summary>Body of POST /api/initiatives (FR-026/FR-027). Required: name, BU, category, AI-DLC level
/// (1–3), owner — validated by the domain <c>Initiative.Create</c> factory.</summary>
public sealed record CreateInitiativeRequest(
    Guid BusinessUnitId,
    string Name,
    string? Description,
    Guid? SponsorPersonId,
    Guid OwnerPersonId,
    Guid CategoryId,
    int AiDlcLevel,
    IReadOnlyList<string>? FunctionsAffected,
    IReadOnlyList<string>? DimensionsAdvanced,
    int? CustomersInProduction,
    string? RiskTier);

/// <summary>Body of PUT /api/initiatives/{id}. Editable fields only — business_unit_id and current_stage
/// are deliberately absent (BU reassignment is out of scope; stage change is HAP-14's forward-only
/// endpoint). HAP-14 appends the governance (FR-030, informational only — §4.2) and technology (FR-032)
/// fields; every one is optional / defaults to empty so a caller that predates this story's UI still
/// round-trips cleanly.</summary>
public sealed record UpdateInitiativeRequest(
    string Name,
    string? Description,
    Guid? SponsorPersonId,
    Guid OwnerPersonId,
    Guid CategoryId,
    int AiDlcLevel,
    IReadOnlyList<string>? FunctionsAffected,
    IReadOnlyList<string>? DimensionsAdvanced,
    int? CustomersInProduction,
    string? RiskTier,
    string? DataSensitivity = null,
    IReadOnlyList<string>? RegulatoryRelevance = null,
    string? ApprovalStatus = null,
    string? Approver = null,
    string? OversightModel = null,
    string? GovernanceNotes = null,
    IReadOnlyList<string>? ModelsProviders = null,
    IReadOnlyList<string>? VendorsTools = null,
    bool UsesCogito = false);

/// <summary>Body of POST /api/initiatives/{id}/stage (FR-028) — the forward-only transition target.
/// There is deliberately no per-transition note/reason field: a correction is a new forward transition,
/// and the AC's own worked examples (contracts/api.md "Register") carry no such field.</summary>
public sealed record StageChangeRequest(string Stage);

/// <summary>Body of POST /api/initiatives/{id}/updates (FR-033) — RAG + optional one-line note,
/// designed for &lt;1 minute entry. <see cref="CustomersInProduction"/> only applies when the
/// initiative's category is customer-deployed (FR-031); otherwise it is silently ignored.</summary>
public sealed record PostWeeklyUpdateRequest(string RagStatus, string? Note, int? CustomersInProduction);

/// <summary>Body of POST /api/initiatives/{id}/nr-lines (FR-029).</summary>
public sealed record CreateNrLineRequest(
    int Year, string Direction, string Recurrence, decimal AmountUsd, string? Description);

/// <summary>Wire shape of one Harris category (FR-027), for filter dropdowns and the create/edit form.</summary>
public sealed record HarrisCategoryResponse(Guid Id, string Key, string Name, bool GroupReported, bool CustomerDeployed);

/// <summary>Wire shape of one business unit, for filter dropdowns and the create/edit form.</summary>
public sealed record BusinessUnitResponse(Guid Id, string Code, string Name);

/// <summary>Wire shape of one initiative, incl. the Harris-mapped stage label the list screen shows
/// (Stage → Harris). <see cref="HarrisStage"/> is null only if the stage map has no row for the current
/// stage (never in a seeded environment).</summary>
public sealed record InitiativeResponse(
    Guid Id,
    Guid BusinessUnitId,
    string Name,
    string? Description,
    Guid? SponsorPersonId,
    Guid OwnerPersonId,
    Guid CreatedByPersonId,
    DateTime RegisteredAt,
    Guid CategoryId,
    int AiDlcLevel,
    IReadOnlyList<string> FunctionsAffected,
    IReadOnlyList<string> DimensionsAdvanced,
    string CurrentStage,
    string? HarrisStage,
    string RagStatus,
    DateTime LastUpdateAt,
    int? CustomersInProduction,
    string RiskTier)
{
    public static InitiativeResponse From(
        Initiative i, IReadOnlyDictionary<InitiativeStage, HarrisStage> stageMap) =>
        new(
            i.Id,
            i.BusinessUnitId,
            i.Name,
            i.Description,
            i.SponsorPersonId,
            i.OwnerPersonId,
            i.CreatedByPersonId,
            i.RegisteredAt,
            i.CategoryId,
            i.AiDlcLevel,
            i.FunctionsAffected,
            i.DimensionsAdvanced,
            i.CurrentStage.ToString(),
            stageMap.TryGetValue(i.CurrentStage, out var harris) ? harris.ToString() : null,
            i.RagStatus.ToString(),
            i.LastUpdateAt,
            i.CustomersInProduction,
            i.RiskTier.ToString());
}

/// <summary>Wire shape of one stage-history entry (data-model.md "InitiativeStageHistory"; FR-028).
/// <see cref="PriorStage"/> is null only for the initial Idea row written at creation.</summary>
public sealed record StageHistoryEntryResponse(Guid Id, string Stage, string? PriorStage, DateTime EnteredAt, Guid EnteredBy);

/// <summary>Wire shape of one NR capture line (FR-029). <see cref="Locked"/> mirrors
/// <c>ReferencedBySubmissionLineId != null</c> — always false on creation (HAP-16 stub; see
/// <c>InitiativeNRLine</c>'s class doc), true once a Harris submission has referenced the line.</summary>
public sealed record NrLineResponse(
    Guid Id, int Year, string Direction, string Recurrence, decimal AmountUsd, string? Description, bool Locked)
{
    public static NrLineResponse From(InitiativeNRLine l) =>
        new(l.Id, l.Year, l.Direction.ToString(), l.Recurrence.ToString(), l.AmountUsd, l.Description,
            l.ReferencedBySubmissionLineId is not null);
}

/// <summary>Wire shape of one weekly update entry (FR-033).</summary>
public sealed record WeeklyUpdateResponse(Guid Id, string RagStatus, string? Note, Guid CreatedBy, DateTime CreatedAt)
{
    public static WeeklyUpdateResponse From(InitiativeWeeklyUpdate u) =>
        new(u.Id, u.RagStatus.ToString(), u.Note, u.CreatedBy, u.CreatedAt);
}

/// <summary>
/// Full detail shape of GET /api/initiatives/{id} (HAP-14; contracts/api.md "Register"). Everything
/// <see cref="InitiativeResponse"/> has, PLUS the governance/technology scalar fields (FR-030/FR-032),
/// PLUS the server-computed <see cref="CanEdit"/> (same logic PUT enforces — lets the UI gate write
/// controls without duplicating auth logic client-side), <see cref="CategoryCustomerDeployed"/> (drives
/// whether the UI shows the customers-in-production field, FR-031), the stage timeline (oldest→newest),
/// the NR lines, and the update trail (newest-first — AC requirement, FR-033).
///
/// <para>Deliberately a SEPARATE record from <see cref="InitiativeResponse"/> (not an extension of it) —
/// the list/create/update endpoints keep the lighter shape; only the single-entity detail read pays for
/// the joined governance/history/lines/updates data.</para>
/// </summary>
public sealed record InitiativeDetailResponse(
    Guid Id,
    Guid BusinessUnitId,
    string Name,
    string? Description,
    Guid? SponsorPersonId,
    Guid OwnerPersonId,
    Guid CreatedByPersonId,
    DateTime RegisteredAt,
    Guid CategoryId,
    int AiDlcLevel,
    IReadOnlyList<string> FunctionsAffected,
    IReadOnlyList<string> DimensionsAdvanced,
    string CurrentStage,
    string? HarrisStage,
    string RagStatus,
    DateTime LastUpdateAt,
    int? CustomersInProduction,
    string RiskTier,
    string DataSensitivity,
    IReadOnlyList<string> RegulatoryRelevance,
    string? ApprovalStatus,
    string? Approver,
    string? OversightModel,
    string? GovernanceNotes,
    IReadOnlyList<string> ModelsProviders,
    IReadOnlyList<string> VendorsTools,
    bool UsesCogito,
    bool CanEdit,
    bool CategoryCustomerDeployed,
    IReadOnlyList<StageHistoryEntryResponse> StageHistory,
    IReadOnlyList<NrLineResponse> NrLines,
    IReadOnlyList<WeeklyUpdateResponse> Updates)
{
    public static InitiativeDetailResponse From(
        Initiative i,
        IReadOnlyDictionary<InitiativeStage, HarrisStage> stageMap,
        bool canEdit,
        bool categoryCustomerDeployed,
        IReadOnlyList<StageHistoryEntryResponse> stageHistory,
        IReadOnlyList<NrLineResponse> nrLines,
        IReadOnlyList<WeeklyUpdateResponse> updates) =>
        new(
            i.Id,
            i.BusinessUnitId,
            i.Name,
            i.Description,
            i.SponsorPersonId,
            i.OwnerPersonId,
            i.CreatedByPersonId,
            i.RegisteredAt,
            i.CategoryId,
            i.AiDlcLevel,
            i.FunctionsAffected,
            i.DimensionsAdvanced,
            i.CurrentStage.ToString(),
            stageMap.TryGetValue(i.CurrentStage, out var harris) ? harris.ToString() : null,
            i.RagStatus.ToString(),
            i.LastUpdateAt,
            i.CustomersInProduction,
            i.RiskTier.ToString(),
            i.DataSensitivity.ToString(),
            i.RegulatoryRelevance,
            i.ApprovalStatus,
            i.Approver,
            i.OversightModel,
            i.GovernanceNotes,
            i.ModelsProviders,
            i.VendorsTools,
            i.UsesCogito,
            canEdit,
            categoryCustomerDeployed,
            stageHistory,
            nrLines,
            updates);
}
