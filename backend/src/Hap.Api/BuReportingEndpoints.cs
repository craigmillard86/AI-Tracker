using System.Security.Claims;
using Hap.Api.Authorization;
using Hap.Api.Identity;
using Hap.Domain.Org;
using Hap.Domain.Register;
using Hap.Domain.Reporting;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api;

/// <summary>
/// BU Harris-reporting capture surfaces (contracts/api.md "BU Lead scope" GET/POST
/// <c>/api/bus/{buId}/declarations</c> and <c>/api/bus/{buId}/metrics</c>; FR-047 weekly AI-DLC
/// declaration, FR-048 monthly Support/SOR metrics). Two distinct scopes, deliberately different from
/// each other:
///
/// <para><b>Write scope (both resources)</b> — <see cref="CanWriteBuAsync"/>: the BU Lead of that BU
/// (hierarchy-derived, re-read from the DB every request per HAP-4 A3 — never a cookie claim) OR an
/// audited <see cref="OrgRole.BuDelegate"/> grant for that BU. This is the ONLY write path for both
/// declarations and metrics.</para>
///
/// <para><b>Read scope</b> differs by resource. Declarations (GET) reads the SAME broader scope as the
/// BU dashboard via <see cref="RollupReads.ReadBuDashboardAsync"/> (BU Lead, Group/Portfolio Leader
/// spanning it, HIG Executive) — the evidence panel is, after all, that dashboard's own figures, and
/// contracts/api.md marks this GET <c>[S]</c> for exactly that reason. Metrics (GET) has no such
/// contract requirement and reuses the narrower write scope instead — simpler, and correct because
/// nothing metrics-side is aggregate/suppression-governed data.</para>
///
/// <para><b>No new score query, by construction (what keeps this story L2, not L3).</b> The evidence
/// panel (measured floor + divergence) is entirely computed from <see cref="RollupReads"/>'s already
/// N&lt;4 + complement-suppressed output — this class never queries the raw assessment or assessment-score
/// tables directly.</para>
/// </summary>
public static class BuReportingEndpoints
{
    public static void MapBuReportingEndpoints(this RouteGroupBuilder api)
    {
        // GET /api/bus/{buId}/declarations — history + measured-evidence panel [S] (FR-047).
        api.MapGet("/bus/{buId:guid}/declarations", async (
            Guid buId, HttpContext http, HapDbContext db, RollupReads rollupReads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            if (!await db.BusinessUnits.AnyAsync(b => b.Id == buId, ct))
            {
                return Results.NotFound();
            }

            NodeAggregateView view;
            try
            {
                view = await rollupReads.ReadBuDashboardAsync(callerId, buId, ct);
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound(); // out of scope — existence-leak convention
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }

            var declarations = await db.BuAiDlcDeclarations.AsNoTracking()
                .Where(d => d.BusinessUnitId == buId)
                .OrderByDescending(d => d.WeekOf)
                .ToListAsync(ct);

            var measuredFloorLevel = MeasuredFloorLevel(view.Current);
            var latest = declarations.FirstOrDefault();
            int? divergence = latest is not null && measuredFloorLevel is int floor
                ? latest.DeclaredLevel - floor
                : null;

            return Results.Ok(new BuDeclarationsResponse(
                buId,
                declarations.Select(BuDeclarationResponse.From).ToList(),
                NodeAggregateResponse.From(view),
                measuredFloorLevel,
                divergence));
        });

        // POST /api/bus/{buId}/declarations — upsert-by-week (FR-047 amended 2026-07-21).
        api.MapPost("/bus/{buId:guid}/declarations", async (
            Guid buId, PostDeclarationRequest request, HttpContext http, HapDbContext db,
            HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            if (!await db.BusinessUnits.AnyAsync(b => b.Id == buId, ct))
            {
                return Results.NotFound();
            }
            if (!await CanWriteBuAsync(callerId, buId, db, hierarchy, ct))
            {
                return Results.Forbid();
            }
            if (!TryParseRagStatus(request.RagStatus, out var rag))
            {
                return Results.UnprocessableEntity(Problem("ragStatus", $"Unrecognised RAG status '{request.RagStatus}'."));
            }

            var weekOf = NormalizeToMonday(request.WeekOf);
            var existing = await db.BuAiDlcDeclarations
                .SingleOrDefaultAsync(d => d.BusinessUnitId == buId && d.WeekOf == weekOf, ct);

            try
            {
                if (existing is null)
                {
                    var created = BuAiDlcDeclaration.Create(
                        buId, weekOf, request.DeclaredLevel, request.NextLevelExpectedDate, rag, request.Note, callerId);
                    db.BuAiDlcDeclarations.Add(created);
                    await db.SaveChangesAsync(ct);
                    return Results.Created(
                        $"/api/bus/{buId}/declarations", BuDeclarationResponse.From(created));
                }

                existing.Update(request.DeclaredLevel, request.NextLevelExpectedDate, rag, request.Note, callerId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(BuDeclarationResponse.From(existing));
            }
            catch (BuReportingValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("declaredLevel", ex.Message));
            }
        });

        // GET /api/bus/{buId}/metrics?month=yyyy-MM-dd — current month, or the prior month's
        // SupportCustomer YTD carried forward when no row exists yet (FR-048).
        api.MapGet("/bus/{buId:guid}/metrics", async (
            Guid buId, DateOnly month, HttpContext http, HapDbContext db,
            HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            if (!await db.BusinessUnits.AnyAsync(b => b.Id == buId, ct))
            {
                return Results.NotFound();
            }
            if (!await CanWriteBuAsync(callerId, buId, db, hierarchy, ct))
            {
                return Results.Forbid();
            }

            var normalisedMonth = NormalizeToFirstOfMonth(month);
            var current = await db.BuMonthlyMetrics.AsNoTracking()
                .SingleOrDefaultAsync(m => m.BusinessUnitId == buId && m.Month == normalisedMonth, ct);
            if (current is not null)
            {
                return Results.Ok(BuMonthlyMetricsResponse.From(buId, current, carriedForward: false));
            }

            // No row for the requested month yet — look one month back and, if found, carry its
            // SupportCustomer YTD figures forward. SupportInternal/SorCalledByOtherApps are current-
            // month-only (FR-048) and start blank regardless (SupportInternal.Empty / null).
            var priorMonth = normalisedMonth.AddMonths(-1);
            var prior = await db.BuMonthlyMetrics.AsNoTracking()
                .SingleOrDefaultAsync(m => m.BusinessUnitId == buId && m.Month == priorMonth, ct);
            if (prior is null)
            {
                return Results.Ok(new BuMonthlyMetricsResponse(
                    buId, normalisedMonth, SupportInternalDto.Empty, SupportCustomerDto.Empty,
                    SorCalledByOtherApps: null, CarriedForward: false, SubmittedBy: null, CreatedAt: null));
            }

            return Results.Ok(new BuMonthlyMetricsResponse(
                buId,
                normalisedMonth,
                SupportInternalDto.Empty,
                SupportCustomerDto.From(prior.SupportCustomer),
                SorCalledByOtherApps: null,
                CarriedForward: true,
                SubmittedBy: null,
                CreatedAt: null));
        });

        // POST /api/bus/{buId}/metrics — upsert-by-month (same shape as declarations).
        api.MapPost("/bus/{buId:guid}/metrics", async (
            Guid buId, PostMetricsRequest request, HttpContext http, HapDbContext db,
            HierarchyRoleResolver hierarchy, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            if (!await db.BusinessUnits.AnyAsync(b => b.Id == buId, ct))
            {
                return Results.NotFound();
            }
            if (!await CanWriteBuAsync(callerId, buId, db, hierarchy, ct))
            {
                return Results.Forbid();
            }

            var month = NormalizeToFirstOfMonth(request.Month);
            var existing = await db.BuMonthlyMetrics
                .SingleOrDefaultAsync(m => m.BusinessUnitId == buId && m.Month == month, ct);

            var supportInternal = request.SupportInternal?.ToDomain();
            var supportCustomer = request.SupportCustomer?.ToDomain();

            try
            {
                if (existing is null)
                {
                    var created = BuMonthlyMetrics.Create(
                        buId, month, supportInternal, supportCustomer, request.SorCalledByOtherApps, callerId);
                    db.BuMonthlyMetrics.Add(created);
                    await db.SaveChangesAsync(ct);
                    return Results.Created(
                        $"/api/bus/{buId}/metrics", BuMonthlyMetricsResponse.From(buId, created, carriedForward: false));
                }

                existing.Update(supportInternal, supportCustomer, request.SorCalledByOtherApps, callerId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(BuMonthlyMetricsResponse.From(buId, existing, carriedForward: false));
            }
            catch (BuReportingValidationException ex)
            {
                return Results.UnprocessableEntity(Problem("metrics", ex.Message));
            }
        });
    }

    /// <summary>
    /// Write authority for both declarations and metrics (deliberately narrower than the declarations
    /// GET scope above): the caller is the BU Lead of <paramref name="buId"/> (hierarchy-derived, re-read
    /// per request — HAP-4 A3) or holds an audited <see cref="OrgRole.BuDelegate"/> grant for that BU.
    /// </summary>
    private static async Task<bool> CanWriteBuAsync(
        Guid callerId, Guid buId, HapDbContext db, HierarchyRoleResolver hierarchy, CancellationToken ct)
    {
        var roles = await hierarchy.ResolveAsync(callerId, ct);
        if (roles.BuLeadOfBusinessUnitId == buId)
        {
            return true;
        }
        return await db.RoleGrants.AnyAsync(
            g => g.PersonId == callerId && g.Role == OrgRole.BuDelegate && g.BusinessUnitId == buId, ct);
    }

    /// <summary>The measured floor level (FR-047's "measured floor"): the MODAL per-person floor level —
    /// the most common entry in the already-published <see cref="AggregateFigures.FloorLevelDistribution"/>
    /// — or null when the current node is suppressed or has no scored population. This is a SUBSTITUTION
    /// for an earlier floor-of-mean computation, not a new query: flooring the MEAN of per-dimension means
    /// across people lets one person's strength in a dimension mask a DIFFERENT person's weakness in
    /// another dimension, which violates the floor rule (root spec Appendix A — the floor is a per-person
    /// property, never a cross-person average). <see cref="AggregateFigures.FloorLevelDistribution"/> is
    /// already the per-person floor, computed once by <c>RollupComputation</c> and consumed here unchanged — still no
    /// new assessment/assessment-score query, so this stays L2. Provisional per Q-033
    /// (docs/decisions/QUESTIONS.md) — the modal choice, and its lower-level tie-break, await owner
    /// ratification at HAP-16.</summary>
    private static int? MeasuredFloorLevel(AggregateReadResult current) =>
        current is AggregateReadResult.Published published && published.Figures.FloorLevelDistribution.Count > 0
            ? ModalFloorLevel(published.Figures.FloorLevelDistribution)
            : null;

    /// <summary>The most common floor level in a floor-level distribution; ties broken toward the LOWER
    /// level (conservative — never let a tie overstate the measured floor). See <see cref="MeasuredFloorLevel"/>.</summary>
    private static int ModalFloorLevel(IReadOnlyDictionary<int, int> floorLevelDistribution)
    {
        var maxCount = floorLevelDistribution.Values.Max();
        return floorLevelDistribution
            .Where(kvp => kvp.Value == maxCount)
            .Min(kvp => kvp.Key);
    }

    /// <summary>Normalises any date to the Monday of its ISO week (FR-047 — one declaration per BU per
    /// week). <see cref="DayOfWeek"/>'s numbering (Sunday=0 .. Saturday=6) needs shifting so Monday is
    /// day 0 of the week before subtracting.</summary>
    private static DateOnly NormalizeToMonday(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    /// <summary>Normalises any date to the first of its calendar month (FR-048).</summary>
    private static DateOnly NormalizeToFirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>Parses <see cref="RagStatus"/> from the wire (FR-047 weekly declaration). No valid
    /// default — every declaration names an explicit RAG status, same convention as
    /// <c>RegisterEndpoints.TryParseRagStatus</c>.</summary>
    private static bool TryParseRagStatus(string? raw, out RagStatus value)
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

/// <summary>Body of POST /api/bus/{buId}/declarations (FR-047).</summary>
public sealed record PostDeclarationRequest(
    DateOnly WeekOf, int DeclaredLevel, DateOnly? NextLevelExpectedDate, string RagStatus, string? Note);

/// <summary>Wire shape of one declaration row.</summary>
public sealed record BuDeclarationResponse(
    Guid Id,
    Guid BusinessUnitId,
    DateOnly WeekOf,
    int DeclaredLevel,
    DateOnly? NextLevelExpectedDate,
    string RagStatus,
    string? Note,
    Guid DeclaredBy,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static BuDeclarationResponse From(BuAiDlcDeclaration d) =>
        new(d.Id, d.BusinessUnitId, d.WeekOf, d.DeclaredLevel, d.NextLevelExpectedDate,
            d.RagStatus.ToString(), d.Note, d.DeclaredBy, d.CreatedAt, d.UpdatedAt);
}

/// <summary>Body of GET /api/bus/{buId}/declarations (FR-047): the declaration history (newest first)
/// alongside the measured-evidence panel [S] and the FR-065 declared-vs-measured divergence value (which
/// HAP-16's Harris submission will report — this story only computes and exposes it).</summary>
public sealed record BuDeclarationsResponse(
    Guid BusinessUnitId,
    IReadOnlyList<BuDeclarationResponse> Declarations,
    NodeAggregateResponse Measured,
    int? MeasuredFloorLevel,
    int? DeclaredVsMeasuredDivergence);

/// <summary>Body of POST /api/bus/{buId}/metrics (FR-048).</summary>
public sealed record PostMetricsRequest(
    DateOnly Month, SupportInternalDto? SupportInternal, SupportCustomerDto? SupportCustomer, string? SorCalledByOtherApps);

/// <summary>Wire shape of the internal-support panel (current-month only, FR-048).</summary>
public sealed record SupportInternalDto(decimal? TimeSavingsPct, string? FewerPeopleNeeded, string? SupportRatioImpact)
{
    public static readonly SupportInternalDto Empty = new(null, null, null);

    public static SupportInternalDto From(Domain.Reporting.SupportInternal s) =>
        new(s.TimeSavingsPct, s.FewerPeopleNeeded, s.SupportRatioImpact);

    public Domain.Reporting.SupportInternal ToDomain() =>
        new(TimeSavingsPct, FewerPeopleNeeded, SupportRatioImpact);
}

/// <summary>Wire shape of the customer-support panel (YTD figures — the ONLY fields FR-048's carry-
/// forward applies to).</summary>
public sealed record SupportCustomerDto(int? CustomersYtd, int? TicketsYtd, int? ResolvedByAiYtd, int? AiAssistedYtd)
{
    public static readonly SupportCustomerDto Empty = new(null, null, null, null);

    public static SupportCustomerDto From(Domain.Reporting.SupportCustomer s) =>
        new(s.CustomersYtd, s.TicketsYtd, s.ResolvedByAiYtd, s.AiAssistedYtd);

    public Domain.Reporting.SupportCustomer ToDomain() =>
        new(CustomersYtd, TicketsYtd, ResolvedByAiYtd, AiAssistedYtd);
}

/// <summary>Body of GET/POST /api/bus/{buId}/metrics (FR-048). <see cref="CarriedForward"/> is true only
/// when the response's <see cref="SupportCustomer"/> figures were pre-filled from the PRIOR month's row
/// because no row exists yet for the requested month — <see cref="SupportInternal"/> and
/// <see cref="SorCalledByOtherApps"/> are never carried forward (current-month-only, FR-048) regardless
/// of this flag. <see cref="SubmittedBy"/>/<see cref="CreatedAt"/> are null on a not-yet-submitted
/// (including carried-forward) response — there is no row yet to attribute them to.</summary>
public sealed record BuMonthlyMetricsResponse(
    Guid BusinessUnitId,
    DateOnly Month,
    SupportInternalDto SupportInternal,
    SupportCustomerDto SupportCustomer,
    string? SorCalledByOtherApps,
    bool CarriedForward,
    Guid? SubmittedBy,
    DateTime? CreatedAt)
{
    public static BuMonthlyMetricsResponse From(Guid businessUnitId, BuMonthlyMetrics m, bool carriedForward) =>
        new(businessUnitId, m.Month, SupportInternalDto.From(m.SupportInternal), SupportCustomerDto.From(m.SupportCustomer),
            m.SorCalledByOtherApps, carriedForward, m.SubmittedBy, m.CreatedAt);
}
