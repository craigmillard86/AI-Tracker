using Hap.Domain.Org;
using Hap.Infrastructure.Directory;

namespace Hap.Api;

/// <summary>
/// Platform-admin surfaces for directory sync and org overrides (contracts/api.md [PA]).
///
/// AUTHORIZATION STATUS (HAP-4): these routes are marked [PA] in the contract and are now fully
/// gated — nested under Program.cs's authorized <c>/api</c> group (401 for an anonymous caller) AND
/// requiring the <c>PlatformAdmin</c> policy (403 for an authenticated non-admin). The 403 gate was
/// closed in this story (not deferred to a later Authorization seam story) after <c>hap-red-team</c>
/// proved a concrete self-escalation path: an authenticated Individual could POST an override that
/// reparented themselves under the org root, after which <c>HierarchyRoleResolver</c>'s depth-from-root
/// rule would label them "Portfolio Leader" (see <c>Hap.Api.Tests/Identity/RedTeamEscalationTests.cs</c>
/// and the story notes). <c>PlatformAdmin</c> is grantable today via <c>LocalDevProvider</c>'s dev-seed
/// bootstrap (QUESTIONS.md Q-013); a general role-grant admin endpoint (a later story) will supersede
/// that bootstrap without touching this gate. These endpoints handle synthetic data only, on a
/// no-network local stack, and are exercised API-only (QUESTIONS.md Q-004).
/// </summary>
public static class AdminEndpoints
{
    /// <summary><paramref name="app"/> is the already-authorized <c>/api</c> group (Program.cs), so
    /// this group is relative: <c>/api</c> + <c>/admin</c> = <c>/api/admin</c>, unchanged from before.
    /// <c>RequireAuthorization("PlatformAdmin")</c> here composes with the outer group's "any
    /// authenticated session" requirement — both must pass.</summary>
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("PlatformAdmin");

        admin.MapPost("/sync", async (DirectoryImportService importer, CancellationToken ct) =>
        {
            var result = await importer.SyncAsync(ct);
            return Results.Ok(result);
        });

        admin.MapGet("/overrides", async (OrgOverrideService overrides, CancellationToken ct) =>
        {
            var rows = await overrides.ListAsync(ct);
            return Results.Ok(rows.Select(OverrideResponse.From));
        });

        admin.MapPost("/overrides", async (
            CreateOverrideRequest request, OrgOverrideService overrides, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!Enum.TryParse<OverrideField>(request.Field, ignoreCase: true, out var field))
            {
                errors["field"] = new[] { $"Unknown override field '{request.Field}'." };
            }
            if (request.PersonId == Guid.Empty)
            {
                errors["personId"] = new[] { "personId is required." };
            }
            if (string.IsNullOrWhiteSpace(request.OverrideValue))
            {
                errors["overrideValue"] = new[] { "overrideValue is required." };
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                errors["reason"] = new[] { "reason is required." };
            }
            if (string.IsNullOrWhiteSpace(request.CreatedBy))
            {
                errors["createdBy"] = new[] { "createdBy is required." };
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors); // 400
            }

            try
            {
                var created = await overrides.CreateAsync(
                    new CreateOverrideCommand(request.PersonId, field, request.OverrideValue, request.Reason, request.CreatedBy),
                    ct);
                return Results.Created($"/api/admin/overrides/{created.Id}", OverrideResponse.From(created));
            }
            catch (PersonNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (OverrideValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });
    }
}

/// <summary>Body of POST /api/admin/overrides.</summary>
public sealed record CreateOverrideRequest(
    Guid PersonId,
    string Field,
    string OverrideValue,
    string Reason,
    string CreatedBy);

/// <summary>Wire shape of an org override.</summary>
public sealed record OverrideResponse(
    Guid Id,
    Guid PersonId,
    string Field,
    string? OriginalValue,
    string OverrideValue,
    string Reason,
    string CreatedBy,
    DateTime CreatedAt)
{
    public static OverrideResponse From(OrgOverride o) =>
        new(o.Id, o.PersonId, o.Field.ToString(), o.OriginalValue, o.OverrideValue, o.Reason, o.CreatedBy, o.CreatedAt);
}
