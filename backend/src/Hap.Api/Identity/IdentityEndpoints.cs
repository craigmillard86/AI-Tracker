using System.Security.Claims;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Identity;

/// <summary>Auth + self-scope endpoints (contracts/api.md "Auth", "Self scope"). <c>/auth/*</c> is
/// intentionally mapped on <paramref name="app"/> directly (outside the authorized <c>/api</c> group)
/// so sign-in itself never requires a session; <c>GET /api/me</c> is mapped on the authorized
/// <paramref name="api"/> group passed in from Program.cs.</summary>
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapGet("/auth/signin", async (IIdentityProvider provider, HttpContext ctx, CancellationToken ct) =>
        {
            await provider.ChallengeAsync(ctx, ct);
        });

        app.MapPost("/auth/signin", async (
            SignInRequest request, IIdentityProvider provider, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserKey))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["userKey"] = new[] { "userKey is required." } });
            }

            try
            {
                var principal = await provider.SignInAsync(ctx, request.UserKey, ct);
                return Results.Ok(new { personId = principal.FindFirstValue("person_id") });
            }
            catch (UnknownSeedUserException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PersonNotSyncedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (InactiveUserException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        app.MapPost("/auth/signout", async (IIdentityProvider provider, HttpContext ctx, CancellationToken ct) =>
        {
            await provider.SignOutAsync(ctx, ct);
            return Results.Ok();
        });

        api.MapGet("/me", async (
            ClaimsPrincipal user, HapDbContext db, HierarchyRoleResolver resolver, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue("person_id"), out var personId))
            {
                return Results.Problem(
                    "Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
            }

            var person = await db.People.SingleAsync(p => p.Id == personId, ct);
            var businessUnitCode = await db.BusinessUnits
                .Where(b => b.Id == person.BusinessUnitId)
                .Select(b => b.Code)
                .SingleAsync(ct);

            var explicitRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            var hierarchyRoles = await resolver.ResolveAsync(personId, ct);

            return Results.Ok(new MeResponse(
                PersonId: person.Id,
                ExternalRef: person.ExternalRef,
                DisplayName: person.DisplayName,
                Email: person.Email,
                JobTitle: person.JobTitle,
                BusinessUnitCode: businessUnitCode,
                ExplicitRoles: explicitRoles,
                ComputedRoles: hierarchyRoles.ToRoleNames(),
                // Cycle domain (FR-002/FR-060) lands in a later story; the field is reserved here so
                // the response shape does not need to change when it does.
                CurrentCycleStatus: null));
        });
    }
}

/// <summary>Body of <c>POST /auth/signin</c>.</summary>
public sealed record SignInRequest(string UserKey);

/// <summary>Wire shape of <c>GET /api/me</c> (contracts/api.md "Self scope").</summary>
public sealed record MeResponse(
    Guid PersonId,
    string ExternalRef,
    string DisplayName,
    string Email,
    string JobTitle,
    string BusinessUnitCode,
    IReadOnlyList<string> ExplicitRoles,
    IReadOnlyList<string> ComputedRoles,
    string? CurrentCycleStatus);
