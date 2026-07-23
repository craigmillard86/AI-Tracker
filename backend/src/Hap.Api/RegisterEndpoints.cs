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

        // GET /api/initiatives/{id} — the full initiative (FR-026). Readable by any authenticated role.
        api.MapGet("/initiatives/{id:guid}", async (
            Guid id, HttpContext http, HapDbContext db, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out _))
            {
                return MissingPrincipal();
            }

            var initiative = await db.Initiatives.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (initiative is null)
            {
                return Results.NotFound();
            }

            var stageMap = await StageMapAsync(db, ct);
            return Results.Ok(InitiativeResponse.From(initiative, stageMap));
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

            var categoryExists = await db.HarrisCategories.AnyAsync(c => c.Id == request.CategoryId, ct);
            if (!categoryExists)
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
                    request.CustomersInProduction,
                    riskTier);

                db.Initiatives.Add(initiative);
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

            var categoryExists = await db.HarrisCategories.AnyAsync(c => c.Id == request.CategoryId, ct);
            if (!categoryExists)
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
                initiative.Edit(
                    request.Name,
                    request.Description,
                    request.SponsorPersonId,
                    request.OwnerPersonId,
                    request.CategoryId,
                    request.AiDlcLevel,
                    request.FunctionsAffected,
                    request.DimensionsAdvanced,
                    request.CustomersInProduction,
                    riskTier);

                await db.SaveChangesAsync(ct);

                var stageMap = await StageMapAsync(db, ct);
                return Results.Ok(InitiativeResponse.From(initiative, stageMap));
            }
            catch (InitiativeValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("initiative", ex.Message));
            }
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
/// endpoint).</summary>
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
    string? RiskTier);

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
