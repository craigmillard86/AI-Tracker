using System.Security.Claims;
using Hap.Api.Authorization;
using Hap.Api.Notifications;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

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

        // BU onboarding (root spec "BU registration": "assign BU to group/portfolio, select
        // applicable framework(s), and configure contractor exclusion" — line 79/158; Platform
        // Admin capability per line 31). BusinessUnit.IsOnboarded has existed since HAP-3 but had
        // no write path anywhere in the codebase; HAP-7 adds this minimal one because its own AC 3
        // (mid-cycle onboarding test) requires onboarding a BU as test setup and no other story
        // owns it (QUESTIONS.md Q-017b). Unaudited: no AuditAction case fits BU onboarding and no
        // FR cited by any story to date calls for one.
        admin.MapPost("/business-units/{id:guid}/onboard", async (Guid id, HapDbContext db, CancellationToken ct) =>
        {
            var bu = await db.BusinessUnits.SingleOrDefaultAsync(b => b.Id == id, ct);
            if (bu is null)
            {
                return Results.NotFound();
            }

            bu.SetOnboarded(true);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new BusinessUnitOnboardResponse(bu.Id, bu.Code, bu.IsOnboarded));
        });

        // GET /api/admin/audit?subject=&action=&from= — read-only audit search (FR-050/FR-053; HAP-12).
        // Platform Admin only (this group). READ ONLY: there is deliberately NO POST/PUT/DELETE audit route
        // anywhere — the log is append-only (immutable AuditLog type + DB trigger + AuditAppendOnlyTests),
        // and a route-table test asserts no audit-mutation endpoint exists.
        admin.MapGet("/audit", async (
            Guid? subject, string? action, DateTime? from, AuditQueryService audit, CancellationToken ct) =>
        {
            AuditAction? parsedAction = null;
            if (!string.IsNullOrWhiteSpace(action))
            {
                if (!Enum.TryParse<AuditAction>(action, ignoreCase: true, out var a))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["action"] = new[] { $"Unknown audit action '{action}'." },
                    }); // 400
                }
                parsedAction = a;
            }

            var rows = await audit.SearchAsync(subject, parsedAction, from, ct);
            return Results.Ok(rows);
        });

        // POST /api/admin/retention/run — GDPR retention erasure (FR-052; HAP-12). Nulls raw self/manager
        // score values in cycles closed > 3 years ago, RETAINING rows (aggregates untouched); writes one
        // RetentionErasure audit row per affected assessment; idempotent. Actor = the running admin (from the
        // session), recorded on every erasure row.
        admin.MapPost("/retention/run", async (HttpContext http, RetentionService retention, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.User.FindFirstValue("person_id"), out var runByPersonId))
            {
                return Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
            }

            var result = await retention.RunAsync(runByPersonId, asOf: null, ct);
            return Results.Ok(new RetentionRunResponse(result.AssessmentsErased, result.ScoreRowsErased));
        });

        // POST /api/admin/notifications/run — runs the notification jobs once, synchronously (HAP-18,
        // research D7: the deterministic test/demo trigger — no background timer to race in a test).
        // Response is a flat Dictionary<string,int> so job types can be added without changing the shape.
        // Runs the FR-037 weekly-update-discipline job and the FR-061 cycle reminder/escalation job (the
        // FR-057 moderation-complete notice is event-driven off the moderation write, not a scheduled job,
        // so it is not run here).
        admin.MapPost("/notifications/run", async (
            NotificationJobService notifications, CycleReminderJob cycleReminders, CancellationToken ct) =>
        {
            var weeklyUpdate = await notifications.RunWeeklyUpdateNagsAsync(asOf: null, ct);
            var cycle = await cycleReminders.RunAsync(asOf: null, ct);
            var counts = new Dictionary<string, int>
            {
                ["WeeklyUpdateOwnerNags"] = weeklyUpdate.OwnerNagsSent,
                ["WeeklyUpdateBuLeadEscalations"] = weeklyUpdate.BuLeadEscalationsSent,
                ["WeeklyUpdateBuLeadEscalationsSkippedNoLead"] = weeklyUpdate.BuLeadEscalationsSkippedNoLead,
                ["CycleReminders"] = cycle.NonResponderRemindersSent,
                ["CycleManagerEscalations"] = cycle.ManagerEscalationsSent,
                ["CycleBuLeadSummaries"] = cycle.BuLeadSummariesSent,
                ["CycleBuLeadSummariesSkippedNoLead"] = cycle.BuLeadSummariesSkippedNoLead,
            };
            return Results.Ok(counts);
        });
    }
}

/// <summary>Body of POST /api/admin/retention/run — how many assessments and score rows had their raw
/// values erased on this run (both zero on an idempotent re-run).</summary>
public sealed record RetentionRunResponse(int AssessmentsErased, int ScoreRowsErased);

/// <summary>Body of POST /api/admin/business-units/{id}/onboard.</summary>
public sealed record BusinessUnitOnboardResponse(Guid Id, string Code, bool IsOnboarded);

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
