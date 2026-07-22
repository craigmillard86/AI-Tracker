using System.Security.Claims;
using Hap.Api.Authorization;
using Hap.Domain.Rollups;

namespace Hap.Api;

/// <summary>
/// Aggregate rollup surfaces (contracts/api.md "BU Lead scope", "Group / Portfolio / HIG Executive scope",
/// and the self "own team summary"; FR-013/015/016/017/018/019/041). All three routes are **[S]** — every
/// figure is N&lt;4 + complement suppressed (FR-014/071) and a suppressed node returns
/// <c>{ "suppressed": true, "reason": … }</c> with NO numeric field. Every data path funnels through the
/// visibility seam's <see cref="RollupReads"/>: the caller's person id comes from the SESSION, scope is
/// decided inside the seam, and an out-of-scope node-addressed request returns 404 (existence-leak
/// convention), never 403.
///
/// <para>No individual-level endpoint exists at any of these scopes by construction (FR-025 clause 2) —
/// these routes return aggregates only.</para>
/// </summary>
public static class RollupEndpoints
{
    public static void MapRollupEndpoints(this RouteGroupBuilder api)
    {
        // GET /api/bus/{buId}/dashboard — BU Lead scope (also reachable by group/portfolio/exec over their span).
        api.MapGet("/bus/{buId:guid}/dashboard", async (
            Guid buId, HttpContext http, RollupReads reads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            try
            {
                var view = await reads.ReadBuDashboardAsync(callerId, buId, ct);
                return Results.Ok(NodeAggregateResponse.From(view));
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound(); // out of scope — existence-leak convention
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });

        // GET /api/org/allhig/rollup — the consolidated all-HIG aggregate (HIG Executive).
        api.MapGet("/org/allhig/rollup", async (HttpContext http, RollupReads reads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            try
            {
                var view = await reads.ReadOrgRollupAsync(callerId, OrgNodeType.AllHig, null, ct);
                return Results.Ok(NodeAggregateResponse.From(view));
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound();
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });

        // GET /api/org/{nodeType}/{nodeId}/rollup — Group / Portfolio (and BU) aggregates only (FR-025).
        api.MapGet("/org/{nodeType}/{nodeId:guid}/rollup", async (
            string nodeType, Guid nodeId, HttpContext http, RollupReads reads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            if (!TryParseOrgNodeType(nodeType, out var parsed) || parsed == OrgNodeType.AllHig)
            {
                // Unknown node type, or AllHig addressed with an id (use /org/allhig/rollup) → 404, no leak.
                return Results.NotFound();
            }
            try
            {
                var view = await reads.ReadOrgRollupAsync(callerId, parsed, nodeId, ct);
                return Results.Ok(NodeAggregateResponse.From(view));
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound();
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });

        // GET /api/me/team/summary — the caller's own team aggregate only (FR spec §2 "Sees").
        api.MapGet("/me/team/summary", async (HttpContext http, RollupReads reads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            try
            {
                var view = await reads.ReadTeamSummaryAsync(callerId, ct);
                return Results.Ok(NodeAggregateResponse.From(view));
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound(); // no team of their own — nothing to summarise
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });

        // GET /api/me/dashboard — the caller's DEFAULT dashboard node (BB2): resolves the richest scope they
        // lead (Exec → all-HIG, Portfolio/Group Leader → their node, BU Lead → their BU, else → own team) and
        // routes through the same scope-checked read, so the router-less shell reaches each persona's scope.
        api.MapGet("/me/dashboard", async (HttpContext http, RollupReads reads, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }
            try
            {
                var view = await reads.ReadDefaultDashboardAsync(callerId, ct);
                return Results.Ok(NodeAggregateResponse.From(view));
            }
            catch (AggregateAccessDeniedException)
            {
                return Results.NotFound(); // no aggregate to show (no leadership, no team)
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });
    }

    private static bool TryParseOrgNodeType(string raw, out OrgNodeType nodeType)
    {
        nodeType = raw.ToLowerInvariant() switch
        {
            "bu" => OrgNodeType.Bu,
            "group" => OrgNodeType.Group,
            "portfolio" => OrgNodeType.Portfolio,
            "allhig" => OrgNodeType.AllHig,
            "team" => OrgNodeType.Team,
            _ => (OrgNodeType)(-1),
        };
        return Enum.IsDefined(typeof(OrgNodeType), nodeType);
    }

    private static bool TryGetPersonId(HttpContext http, out Guid personId) =>
        Guid.TryParse(http.User.FindFirstValue("person_id"), out personId);

    private static IResult MissingPrincipal() =>
        Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
}

/// <summary>The dashboard figure-set on the wire (FR-015/016/019/068). Present ONLY for a published node —
/// <see cref="NodeAggregateResponse.Figures"/> is null for a suppressed one, so no number is ever emitted for
/// a suppressed aggregate (F2 / FR-071).</summary>
public sealed record AggregateFiguresResponse(
    int N,
    IReadOnlyDictionary<string, double> PerDimensionMean,
    IReadOnlyDictionary<int, int> FloorLevelDistribution,
    double CompletionPct,
    double UnmoderatedPct)
{
    public static AggregateFiguresResponse From(AggregateFigures f) =>
        new(f.N, f.PerDimensionMean, f.FloorLevelDistribution, f.CompletionPct, f.UnmoderatedPct);
}

/// <summary>One framework dimension's metadata (key/name/order) — framework data, safe for any node.</summary>
public sealed record DimensionMetaResponse(string Key, string Name, int DisplayOrder)
{
    public static DimensionMetaResponse From(DimensionMetaView d) => new(d.Key, d.Name, d.DisplayOrder);
}

/// <summary>One trend point: a closed cycle and its (suppression-projected) figures — <see cref="Figures"/>
/// null when that cycle's node was suppressed.</summary>
public sealed record TrendPointResponse(
    Guid CycleId, string CycleName, bool Suppressed, string? SuppressionReason, AggregateFiguresResponse? Figures)
{
    public static TrendPointResponse From(TrendPointView t)
    {
        var (suppressed, reason, figures) = Unpack(t.Result);
        return new TrendPointResponse(t.CycleId, t.CycleName, suppressed, reason, figures);
    }

    internal static (bool Suppressed, string? Reason, AggregateFiguresResponse? Figures) Unpack(AggregateReadResult result) =>
        result switch
        {
            AggregateReadResult.Published p => (false, null, AggregateFiguresResponse.From(p.Figures)),
            AggregateReadResult.Suppressed s => (true, s.Reason, null),
            _ => (true, "N<4", null),
        };
}

/// <summary>Body of the three rollup endpoints (contracts/api.md [S]). A suppressed current node carries
/// <c>suppressed=true</c> + reason and <c>figures=null</c>. <see cref="Initiatives"/> is a null stub until
/// HAP-13 ships the register (FR-041) — the UI renders "—".</summary>
public sealed record NodeAggregateResponse(
    string NodeType,
    Guid? NodeRef,
    string NodeName,
    Guid CycleId,
    string CycleName,
    string CycleState,
    bool Live,
    bool Suppressed,
    string? SuppressionReason,
    AggregateFiguresResponse? Figures,
    IReadOnlyList<DimensionMetaResponse> Dimensions,
    IReadOnlyList<TrendPointResponse> Trend,
    object? Initiatives)
{
    public static NodeAggregateResponse From(NodeAggregateView v)
    {
        var (suppressed, reason, figures) = TrendPointResponse.Unpack(v.Current);
        return new NodeAggregateResponse(
            v.NodeType.ToString(),
            v.NodeRef,
            v.NodeName,
            v.CycleId,
            v.CycleName,
            v.CycleState,
            v.Live,
            suppressed,
            reason,
            figures,
            v.Dimensions.Select(DimensionMetaResponse.From).ToList(),
            v.Trend.Select(TrendPointResponse.From).ToList(),
            Initiatives: null); // FR-041 — HAP-13
    }
}
