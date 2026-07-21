using Hap.Domain.Org;
using Hap.Infrastructure.Directory;

namespace Hap.Api;

/// <summary>
/// Platform-admin surfaces for directory sync and org overrides (contracts/api.md [PA]).
///
/// AUTHORIZATION DEFERRAL (HAP-3, wave 0): these routes are marked [PA] in the contract, but
/// the <c>IIdentityProvider</c> that supplies the caller's role lands in a later story (HAP-4/5).
/// Until then the group carries no auth filter — the single extension point below is where the
/// [PA] guard attaches. These endpoints handle synthetic data only, on a no-network local stack,
/// and are exercised API-only (QUESTIONS.md Q-004). They MUST be gated before Gate G1.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // TODO(HAP-4/5): attach .RequirePlatformAdmin() here once IIdentityProvider is wired.
        var admin = app.MapGroup("/api/admin");

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
