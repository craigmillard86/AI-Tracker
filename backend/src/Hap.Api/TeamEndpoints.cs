using System.Security.Claims;
using Hap.Api.Authorization;

namespace Hap.Api;

/// <summary>
/// Manager-scope moderation surfaces (contracts/api.md "Manager scope"; FR-008/009/010/012/063/069).
/// The review queue, the [A] individual-view read of a direct report, and the moderation write. Every
/// data path funnels through the visibility seam's <see cref="ManagerModerationService"/> — the caller's
/// person id comes from the SESSION (never a body/query parameter), and the only person a route addresses
/// is resolved and authorised inside the seam. Out-of-reach person-addressed requests return 404 (not
/// 403), the existence-leak convention, with no audit row (contracts/api.md scope-enforcement note).
///
/// <para>Exception → status mapping mirrors the self path: the seam surfaces its OWN typed exceptions
/// (the moderation service translates the domain's forward-only/validation exceptions into seam ones), so
/// this endpoint never references the domain assessment namespace — which the boundary guard forbids
/// outside the seam.</para>
/// </summary>
public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this RouteGroupBuilder api)
    {
        var team = api.MapGroup("/team");

        // GET /api/team/reviews — the caller's review queue (state + leave flags; no score data).
        team.MapGet("/reviews", async (HttpContext http, ManagerModerationService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            try
            {
                var view = await svc.GetReviewQueueAsync(callerId, ct);
                return Results.Ok(TeamReviewsResponse.From(view));
            }
            catch (NoCurrentCycleException)
            {
                // No cycle to review right now — 404, matching the self form's "no open cycle" state.
                return Results.NotFound();
            }
        });

        // GET /api/team/members/{personId}/assessment — [A] individual-view read of a direct report.
        // Writes exactly one IndividualView audit row (fail-closed) inside the seam on success.
        team.MapGet("/members/{personId:guid}/assessment", async (
            Guid personId, HttpContext http, ManagerModerationService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            try
            {
                var view = await svc.GetMemberAssessmentAsync(callerId, personId, ct);
                // Authorised but no assessment row this cycle → 404 (no individual data to view, no audit).
                return view is null ? Results.NotFound() : Results.Ok(MemberAssessmentResponse.From(view));
            }
            catch (AssessmentAccessDeniedException)
            {
                // Out of the caller's reach — 404 (existence-leak convention), no audit row was written.
                return Results.NotFound();
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
        });

        // PUT /api/team/reviews/{assessmentId} — the moderation write (Submitted → Moderated).
        team.MapPut("/reviews/{assessmentId:guid}", async (
            Guid assessmentId, ModerateReviewRequest request, HttpContext http, ManagerModerationService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var callerId))
            {
                return MissingPrincipal();
            }

            var decisions = (request.Decisions ?? Array.Empty<ModerateDecision>())
                .Select(d => new ManagerScoreInput(d.DimensionId, d.ManagerScore, d.Comment))
                .ToList();

            try
            {
                await svc.ModerateAsync(callerId, assessmentId, decisions, ct);
                return Results.NoContent();
            }
            catch (AssessmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AssessmentAccessDeniedException)
            {
                // Not the subject's reviewer of record, or lacking read capability over them — 404
                // (existence-leak), no audit row written.
                return Results.NotFound();
            }
            catch (NoCurrentCycleException)
            {
                return Results.NotFound();
            }
            catch (AssessmentCycleLockedException ex)
            {
                // Post-close moderation without a late override (Q-017a submission lock).
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status423Locked);
            }
            catch (ModerationNotSubmittedException ex)
            {
                // Not currently Submitted — nothing to moderate / already moderated.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ModerationConflictException ex)
            {
                // Lost an optimistic-concurrency race with another moderation of the same assessment.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (ModerationValidationException ex)
            {
                // Divergence ≥ 2 without a comment (FR-009), or a manager score out of 0–3.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (ModerationDimensionException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });
    }

    private static bool TryGetPersonId(HttpContext http, out Guid personId) =>
        Guid.TryParse(http.User.FindFirstValue("person_id"), out personId);

    private static IResult MissingPrincipal() =>
        Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
}

/// <summary>Body of PUT /api/team/reviews/{assessmentId} — the manager's per-dimension decisions.
/// Dimensions omitted from the list are defaulted server-side (FR-063 carry-forward / adopt-self).</summary>
public sealed record ModerateReviewRequest(IReadOnlyList<ModerateDecision> Decisions);

/// <summary>One dimension's manager decision: the dimension, the 0–3 moderated score, and an optional
/// comment (mandatory server-side when |self − manager| ≥ 2, FR-009).</summary>
public sealed record ModerateDecision(Guid DimensionId, int ManagerScore, string? Comment);

/// <summary>One report in the review-queue response (state + leave flag only; no score data).</summary>
public sealed record TeamReviewItemResponse(
    Guid? AssessmentId, Guid PersonId, string DisplayName, bool OnLeave, string State, bool CanModerate)
{
    public static TeamReviewItemResponse From(TeamReviewItemView v) =>
        new(v.AssessmentId, v.PersonId, v.DisplayName, v.OnLeave, v.State, v.CanModerate);
}

/// <summary>Body of GET /api/team/reviews.</summary>
public sealed record TeamReviewsResponse(
    Guid CycleId, string CycleName, bool IsManager, IReadOnlyList<TeamReviewItemResponse> Reviews)
{
    public static TeamReviewsResponse From(TeamReviewsView v) =>
        new(v.CycleId, v.CycleName, v.IsManager, v.Reviews.Select(TeamReviewItemResponse.From).ToList());
}

/// <summary>One 0–3 descriptor on the wire (level, framework-wide level name, dimension text).</summary>
public sealed record ModerationLevelResponse(int Level, string LevelName, string DescriptorText);

/// <summary>One dimension on the moderation form: descriptors from framework data, the report's self
/// score/evidence, the prior-cycle self + moderated scores (FR-063 inputs), any existing moderated value,
/// and the computed carry-forward/adopt default the UI pre-fills.</summary>
public sealed record MemberDimensionResponse(
    Guid DimensionId,
    string Key,
    string Name,
    int DisplayOrder,
    IReadOnlyList<ModerationLevelResponse> Levels,
    int SelfScore,
    string? SelfEvidence,
    int? PriorSelfScore,
    int? PriorManagerScore,
    int? ManagerScore,
    string? ManagerComment,
    int DefaultManagerScore,
    bool DefaultCommentRequired);

/// <summary>Body of GET /api/team/members/{personId}/assessment.</summary>
public sealed record MemberAssessmentResponse(
    Guid AssessmentId,
    Guid PersonId,
    string DisplayName,
    Guid CycleId,
    string CycleName,
    string State,
    bool OnLeave,
    bool Editable,
    int CommentThreshold,
    IReadOnlyList<MemberDimensionResponse> Dimensions)
{
    public static MemberAssessmentResponse From(MemberAssessmentView v) =>
        new(
            v.AssessmentId,
            v.PersonId,
            v.DisplayName,
            v.CycleId,
            v.CycleName,
            v.State,
            v.OnLeave,
            v.Editable,
            v.CommentThreshold,
            v.Dimensions
                .Select(d => new MemberDimensionResponse(
                    d.DimensionId,
                    d.Key,
                    d.Name,
                    d.DisplayOrder,
                    d.Levels.Select(l => new ModerationLevelResponse(l.Level, l.LevelName, l.DescriptorText)).ToList(),
                    d.SelfScore,
                    d.SelfEvidence,
                    d.PriorSelfScore,
                    d.PriorManagerScore,
                    d.ManagerScore,
                    d.ManagerComment,
                    d.DefaultManagerScore,
                    d.DefaultCommentRequired))
                .ToList());
}
