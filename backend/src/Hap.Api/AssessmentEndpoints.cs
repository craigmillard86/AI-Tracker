using System.Security.Claims;
using Hap.Api.Authorization;

namespace Hap.Api;

/// <summary>
/// Self-scope assessment surfaces (contracts/api.md "Self scope"; FR-007/062/066): the current-cycle
/// self-assessment form data, the partial-progress upsert, and submit. All three are on the authorized
/// <c>/api</c> group and derive the subject from the SESSION — none takes a person id in the route or
/// body, so there is no surface through which a caller could read or write another person's assessment
/// (the mandatory L3 cross-person attack is structurally answered here, not just checked). Reads of
/// one's own data are not audited (contracts/api.md); the data path is seam-only via
/// <see cref="SelfAssessmentService"/>.
/// </summary>
public static class AssessmentEndpoints
{
    /// <summary>The FR-066 purpose-limitation copy key. The API returns a stable key, not prose; the
    /// client renders the externalised string (FR-067) so copy never lives in two places.</summary>
    private const string PurposeLimitationKey = "assessment.purposeLimitation";

    public static void MapAssessmentEndpoints(this RouteGroupBuilder api)
    {
        var self = api.MapGroup("/me/assessment");

        self.MapGet("", async (HttpContext http, SelfAssessmentService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var personId))
            {
                return MissingPrincipal();
            }

            try
            {
                var view = await svc.GetAsync(personId, ct);
                return Results.Ok(SelfAssessmentResponse.From(view, PurposeLimitationKey));
            }
            catch (NoCurrentCycleException)
            {
                // No cycle to assess right now — 404 rather than an empty 200, so the client shows a
                // "no open cycle" state rather than an empty form.
                return Results.NotFound();
            }
            catch (NotInvitedToCycleException)
            {
                // Not a participant of this cycle — same 404 as no-cycle (a non-participant has no
                // assessment to view; the person-addressed 404 convention avoids leaking participation).
                return Results.NotFound();
            }
        });

        self.MapPut("/scores", async (
            UpsertScoresRequest request, HttpContext http, SelfAssessmentService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var personId))
            {
                return MissingPrincipal();
            }

            var inputs = (request.Scores ?? Array.Empty<ScoreEntry>())
                .Select(s => new SelfScoreInput(s.DimensionId, s.Score, s.Evidence))
                .ToList();

            try
            {
                await svc.UpsertScoresAsync(personId, inputs, ct);
                return Results.NoContent();
            }
            catch (NoCurrentCycleException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (NotInvitedToCycleException)
            {
                return Results.NotFound();
            }
            catch (AssessmentCycleLockedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status423Locked);
            }
            catch (AssessmentAlreadySubmittedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (SelfScoreRangeException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (SelfScoreDimensionException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        self.MapPost("/submit", async (HttpContext http, SelfAssessmentService svc, CancellationToken ct) =>
        {
            if (!TryGetPersonId(http, out var personId))
            {
                return MissingPrincipal();
            }

            try
            {
                await svc.SubmitAsync(personId, ct);
                return Results.NoContent();
            }
            catch (NoCurrentCycleException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (NotInvitedToCycleException)
            {
                return Results.NotFound();
            }
            catch (AssessmentCycleLockedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status423Locked);
            }
            catch (AssessmentAlreadySubmittedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (AssessmentIncompleteException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });
    }

    // Matches IdentityEndpoints/CycleEndpoints: a missing/malformed person_id claim is a broken
    // session (500), never a silent crash.
    private static bool TryGetPersonId(HttpContext http, out Guid personId) =>
        Guid.TryParse(http.User.FindFirstValue("person_id"), out personId);

    private static IResult MissingPrincipal() =>
        Results.Problem("Session principal is missing person_id.", statusCode: StatusCodes.Status500InternalServerError);
}

/// <summary>Body of PUT /api/me/assessment/scores — a partial set of dimension scores to upsert.</summary>
public sealed record UpsertScoresRequest(IReadOnlyList<ScoreEntry> Scores);

/// <summary>One dimension's self input: the dimension, the 0–3 level, optional evidence.</summary>
public sealed record ScoreEntry(Guid DimensionId, int Score, string? Evidence);

/// <summary>One 0–3 descriptor on the wire (level, framework-wide level name, dimension text).</summary>
public sealed record SelfLevelResponse(int Level, string LevelName, string DescriptorText);

/// <summary>One dimension on the self-assessment form: descriptors from data, current self
/// score/evidence, and the prior-cycle score for pre-population (FR-062).</summary>
public sealed record SelfDimensionResponse(
    Guid DimensionId,
    string Key,
    string Name,
    int DisplayOrder,
    IReadOnlyList<SelfLevelResponse> Levels,
    int? SelfScore,
    string? SelfEvidence,
    int? PriorScore);

/// <summary>Body of GET /api/me/assessment. <c>Editable</c> is false when the cycle no longer accepts
/// this caller's writes (Closed without a late override) or the assessment is already submitted — the
/// client renders the form read-only rather than discovering the lock only on Save/Submit.</summary>
public sealed record SelfAssessmentResponse(
    Guid CycleId,
    string CycleName,
    string CycleState,
    bool Submitted,
    bool Editable,
    string PurposeLimitationKey,
    int DimensionCount,
    IReadOnlyList<SelfDimensionResponse> Dimensions)
{
    public static SelfAssessmentResponse From(SelfAssessmentView view, string purposeLimitationKey) =>
        new(
            view.CycleId,
            view.CycleName,
            view.CycleState,
            view.Submitted,
            view.Editable,
            purposeLimitationKey,
            view.Dimensions.Count,
            view.Dimensions
                .Select(d => new SelfDimensionResponse(
                    d.DimensionId,
                    d.Key,
                    d.Name,
                    d.DisplayOrder,
                    d.Levels.Select(l => new SelfLevelResponse(l.Level, l.LevelName, l.DescriptorText)).ToList(),
                    d.SelfScore,
                    d.SelfEvidence,
                    d.PriorScore))
                .ToList());
}
