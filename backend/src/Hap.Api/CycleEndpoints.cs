using System.Security.Claims;
using Hap.Api.Authorization;
using Hap.Domain.Cycles;
using Hap.Domain.Org;
using Hap.Infrastructure.Cycles;

namespace Hap.Api;

/// <summary>
/// Cycle management surfaces (contracts/api.md "[PA] POST /api/cycles…" + late-override; FR-002/
/// 003/004/005/006/060). <c>POST /cycles</c>, <c>/open</c>, and <c>/close</c> are Platform-Admin
/// only, nested under the same admin-gated group pattern as <see cref="AdminEndpoints"/> and
/// <see cref="FrameworkEndpoints"/>. <c>late-override</c> is deliberately NOT under that group:
/// per contracts/api.md it is "Manager-or-admin... (manager variant scoped to own team)", so it
/// sits directly on the authenticated <c>/api</c> group and does its own scope check inline —
/// Platform Admin (any person) or a Manager targeting their own direct report only, using the
/// HAP-5 seam's <see cref="OrgGraphLoader"/> rather than reinventing the check (per story
/// instruction). Admin surface is API-only (QUESTIONS.md Q-004) — no UI in this story.
/// </summary>
public static class CycleEndpoints
{
    /// <summary><paramref name="api"/> is the already-authorized <c>/api</c> group (Program.cs).</summary>
    public static void MapCycleEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/cycles").RequireAuthorization("PlatformAdmin");

        admin.MapPost("", async (CreateCycleRequest request, CycleService svc, CancellationToken ct) =>
        {
            try
            {
                var cycle = await svc.CreateAsync(
                    request.FrameworkVersionId, request.Name, request.ContractorExclusionEnabled, ct);
                return Results.Created($"/api/cycles/{cycle.Id}", CycleResponse.From(cycle));
            }
            catch (FrameworkVersionNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        admin.MapPost("/{id:guid}/open", async (Guid id, CycleService svc, CancellationToken ct) =>
        {
            try
            {
                var result = await svc.OpenAsync(id, ct);
                return Results.Ok(CycleOpenResponse.From(result));
            }
            catch (CycleNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (DuplicateOpenCycleException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (CycleStateTransitionException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        admin.MapPost("/{id:guid}/close", async (Guid id, CycleService svc, CancellationToken ct) =>
        {
            try
            {
                var cycle = await svc.CloseAsync(id, ct);
                return Results.Ok(CycleResponse.From(cycle));
            }
            catch (CycleNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (CycleStateTransitionException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        api.MapPost("/cycles/{id:guid}/late-override", async (
            Guid id, LateOverrideRequest request, HttpContext http, CycleService svc, OrgGraphLoader graphLoader, CancellationToken ct) =>
        {
            // Matches IdentityEndpoints.cs's convention: a missing/malformed person_id claim is a
            // broken session, not a caller error — 500, not a silent NRE crash.
            if (!Guid.TryParse(http.User.FindFirstValue("person_id"), out var callerId))
            {
                return Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
            }
            var isPlatformAdmin = http.User.IsInRole(nameof(OrgRole.PlatformAdmin));

            // Target existence/scope is checked for BOTH paths (L2 panel round-1 blocking-adjacent
            // advisory): previously a nonexistent PersonId under the PlatformAdmin path fell through
            // to an unhandled DbUpdateException (500) instead of a clean 404.
            var graph = await graphLoader.LoadAsync(ct);
            var target = graph.Find(request.PersonId);

            if (isPlatformAdmin)
            {
                if (target is null)
                {
                    return Results.NotFound();
                }
            }
            else
            {
                // "own directs only" (FR-002) is a DIRECT-report check on an ACTIVE report,
                // deliberately not the full upward ChainResolver ancestor grant (that would let a
                // grandmanager override a grandchild's submission — too permissive for this AC).
                // The IsActive check (L2 panel advisory) mirrors ChainResolver's own convention: a
                // manager should not be able to grant a late override for a report who has since
                // departed. Out-of-scope returns 404, matching contracts/api.md's "out-of-scope
                // person-addressed requests return 404, not 403, to avoid existence leaks" convention.
                if (target is null || target.ManagerPersonId != callerId || !target.IsActive)
                {
                    return Results.NotFound();
                }
            }

            try
            {
                var grant = await svc.GrantLateOverrideAsync(
                    id, request.PersonId, callerId, isPlatformAdmin ? "PlatformAdmin" : "Manager", ct);
                return Results.Ok(LateOverrideResponse.From(grant));
            }
            catch (CycleNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });
    }
}

/// <summary>Body of POST /api/cycles.</summary>
public sealed record CreateCycleRequest(Guid FrameworkVersionId, string Name, bool ContractorExclusionEnabled = true);

/// <summary>Wire shape of a cycle.</summary>
public sealed record CycleResponse(
    Guid Id, Guid FrameworkVersionId, string Name, string State,
    bool ContractorExclusionEnabled, DateTime? OpensAt, DateTime? ClosesAt)
{
    public static CycleResponse From(Cycle c) =>
        new(c.Id, c.FrameworkVersionId, c.Name, c.State.ToString(), c.ContractorExclusionEnabled, c.OpensAt, c.ClosesAt);
}

/// <summary>Body of POST /api/cycles/{id}/open — the cycle plus the invitation-generation counts
/// (HAP-7 AC 1: "counts asserted against synth data").</summary>
public sealed record CycleOpenResponse(
    CycleResponse Cycle, int TotalActivePeople, int Invited, int ExcludedContractor, int ExcludedNotOnboarded)
{
    public static CycleOpenResponse From(CycleOpenResult result) =>
        new(CycleResponse.From(result.Cycle), result.TotalActivePeople, result.Invited,
            result.ExcludedContractor, result.ExcludedNotOnboarded);
}

/// <summary>Body of POST /api/cycles/{id}/late-override — the person the override is FOR (not
/// the caller granting it, which is derived from the session).</summary>
public sealed record LateOverrideRequest(Guid PersonId);

/// <summary>Wire shape of a granted late override.</summary>
public sealed record LateOverrideResponse(
    Guid Id, Guid CycleId, Guid PersonId, Guid GrantedByPersonId, string GrantedByRole, DateTime CreatedAt)
{
    public static LateOverrideResponse From(CycleLateOverride o) =>
        new(o.Id, o.CycleId, o.PersonId, o.GrantedByPersonId, o.GrantedByRole, o.CreatedAt);
}
