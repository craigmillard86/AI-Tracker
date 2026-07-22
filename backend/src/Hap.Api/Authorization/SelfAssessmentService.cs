using Hap.Domain.Assessments;
using Hap.Domain.Cycles;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The self-scope assessment workflow (contracts/api.md "Self scope"; FR-007/062/066). Lives in the
/// visibility seam because it reads and writes individual assessment data — but it is structurally
/// self-only: every operation takes the caller's own <c>personId</c> as BOTH actor and subject, so
/// there is no parameter through which one person could reach another's assessment (the mandatory QA
/// cross-person attack has no surface here). Individual reads of one's OWN data are not audited
/// (contracts/api.md: "Viewing your own data is not audited"); the data path is still seam-only.
///
/// <para><b>Submission lock (Q-017a, HAP-7 handoff — binding).</b> Both the score-write path
/// (<see cref="UpsertScoresAsync"/>) and the submit path (<see cref="SubmitAsync"/>) consult
/// <see cref="Cycle.AllowsSubmission"/> with the live late-override verdict from
/// <see cref="CycleService.HasLateOverrideAsync"/> before touching storage: a post-close write is
/// rejected (<see cref="AssessmentCycleLockedException"/> → 423) unless a late override exists. This is
/// the ONLY place that obligation is discharged in the system, so it must not be silently dropped.</para>
/// </summary>
public sealed class SelfAssessmentService
{
    private readonly HapDbContext _db;
    private readonly ISelfAssessmentStore _store;
    private readonly CycleService _cycles;

    public SelfAssessmentService(HapDbContext db, ISelfAssessmentStore store, CycleService cycles)
    {
        _db = db;
        _store = store;
        _cycles = cycles;
    }

    /// <summary>The caller's self-assessment for the current cycle: framework dimensions + descriptors
    /// (from data — FR-001), current self scores, and prior-cycle scores for pre-population (FR-062).
    /// Throws <see cref="NoCurrentCycleException"/> (→ 404) when no cycle is available, and
    /// <see cref="NotInvitedToCycleException"/> (→ 404) when the caller is not an invited, non-excluded
    /// participant (FR-002/FR-005).</summary>
    public async Task<SelfAssessmentView> GetAsync(Guid personId, CancellationToken ct = default)
    {
        var cycle = await CurrentCycleAsync(ct);
        await EnsureInvitedAsync(cycle, personId, ct);
        var dimensions = await DimensionsAsync(cycle.FrameworkVersionId, ct);

        var current = await _store.GetSelfAsync(personId, cycle.Id, ct);
        var currentByDim = (current?.Scores ?? Array.Empty<AssessmentScore>())
            .ToDictionary(s => s.DimensionId);

        var priorCycleId = await PriorCycleIdAsync(cycle, ct);
        var priorByDim = priorCycleId is null
            ? new Dictionary<Guid, AssessmentScore>()
            : (await _store.GetSelfScoresForCycleAsync(personId, priorCycleId.Value, ct))
                .ToDictionary(s => s.DimensionId);

        var dimensionViews = dimensions
            .Select(d =>
            {
                currentByDim.TryGetValue(d.Id, out var cur);
                priorByDim.TryGetValue(d.Id, out var prior);
                return new SelfDimensionView(
                    DimensionId: d.Id,
                    Key: d.Key,
                    Name: d.Name,
                    DisplayOrder: d.DisplayOrder,
                    Levels: d.Levels,
                    SelfScore: cur?.SelfScore,
                    SelfEvidence: cur?.SelfEvidence,
                    PriorScore: prior?.SelfScore);
            })
            .ToList();

        var submitted = current?.Assessment.State
            is AssessmentState.Submitted or AssessmentState.Moderated or AssessmentState.AutoAdopted;

        // Editable = the submission lock currently permits a write AND it is not already submitted, so
        // the client can render a non-Open cycle (no late override) as read-only up front rather than
        // surfacing a 423 only on Save/Submit (panel advisory).
        var hasLateOverride = await _cycles.HasLateOverrideAsync(cycle.Id, personId, ct);
        var editable = cycle.AllowsSubmission(hasLateOverride) && !submitted;

        return new SelfAssessmentView(
            CycleId: cycle.Id,
            CycleName: cycle.Name,
            CycleState: cycle.State.ToString(),
            Submitted: submitted,
            Editable: editable,
            Dimensions: dimensionViews);
    }

    /// <summary>Upserts the caller's own scores/evidence for the current cycle (partial progress
    /// allowed). Gates on cycle participation and the submission lock; rejects a write to an
    /// already-submitted assessment (409), an out-of-range score (422), or a dimension not in the
    /// cycle's framework / a duplicated dimension (422) — all before any storage write.</summary>
    public async Task UpsertScoresAsync(
        Guid personId, IReadOnlyList<SelfScoreInput> scores, CancellationToken ct = default)
    {
        // Fail fast on an out-of-range score before any write (the domain entity re-validates as a
        // backstop). Surfaced as a seam exception so the endpoint never depends on the domain namespace.
        foreach (var input in scores)
        {
            if (input.Score is < AssessmentScore.MinScore or > AssessmentScore.MaxScore)
            {
                throw new SelfScoreRangeException(input.DimensionId, input.Score);
            }
        }

        var cycle = await CurrentCycleAsync(ct);
        await EnsureInvitedAsync(cycle, personId, ct);
        await EnsureSubmissionAllowedAsync(cycle, personId, ct);
        await ValidateDimensionsAsync(cycle.FrameworkVersionId, scores, ct);
        await _store.UpsertSelfScoresAsync(personId, cycle.Id, scores, ct);
    }

    /// <summary>Submits the caller's own assessment for the current cycle (InProgress → Submitted).
    /// Gates on cycle participation and the submission lock; requires every dimension scored (422) and
    /// rejects a double-submit (409).</summary>
    public async Task SubmitAsync(Guid personId, CancellationToken ct = default)
    {
        var cycle = await CurrentCycleAsync(ct);
        await EnsureInvitedAsync(cycle, personId, ct);
        await EnsureSubmissionAllowedAsync(cycle, personId, ct);

        var requiredDimensionIds = await _db.Dimensions
            .Where(d => d.FrameworkVersionId == cycle.FrameworkVersionId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        await _store.SubmitSelfAsync(personId, cycle.Id, requiredDimensionIds, ct);
    }

    // The cycle a self-assessment currently acts on: the single Open cycle if one exists (FR-002: one
    // Open per framework; this build is single-framework), otherwise the most-recently-opened Closed
    // cycle — the post-close late-override window (Q-017a). A write against a Closed cycle is then gated
    // by the submission lock (EnsureSubmissionAllowedAsync), which rejects it unless an override exists.
    // Draft cycles are never "current" (nothing to assess yet).
    private async Task<Cycle> CurrentCycleAsync(CancellationToken ct)
    {
        var open = await _db.Cycles
            .Where(c => c.State == CycleState.Open)
            .OrderByDescending(c => c.OpensAt)
            .FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            return open;
        }

        var mostRecentClosed = await _db.Cycles
            .Where(c => c.State == CycleState.Closed)
            .OrderByDescending(c => c.OpensAt)
            .FirstOrDefaultAsync(ct);
        return mostRecentClosed ?? throw new NoCurrentCycleException();
    }

    // The immediately-preceding cycle for the same framework version (FR-062 "previous cycle"). Draft
    // cycles never held scores, so they are excluded.
    private async Task<Guid?> PriorCycleIdAsync(Cycle current, CancellationToken ct) =>
        await _db.Cycles
            .Where(c => c.FrameworkVersionId == current.FrameworkVersionId
                        && c.Id != current.Id
                        && c.State != CycleState.Draft
                        && c.OpensAt < current.OpensAt)
            .OrderByDescending(c => c.OpensAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

    private async Task EnsureSubmissionAllowedAsync(Cycle cycle, Guid personId, CancellationToken ct)
    {
        var hasLateOverride = await _cycles.HasLateOverrideAsync(cycle.Id, personId, ct);
        if (!cycle.AllowsSubmission(hasLateOverride))
        {
            throw new AssessmentCycleLockedException(cycle.Id);
        }
    }

    // Only an invited, non-excluded participant of the cycle may read or write a self-assessment
    // (FR-002/FR-005): a contractor excluded at open, or a person in a not-onboarded BU, has an
    // Excluded=true invitation row (or none at all) and must not be able to land an Assessment row that
    // every downstream rollup/Harris consumer would then ingest. The invitation set is frozen at open
    // (CycleInvitation), so this is a single indexed lookup on (CycleId, PersonId).
    private async Task EnsureInvitedAsync(Cycle cycle, Guid personId, CancellationToken ct)
    {
        var invited = await _db.CycleInvitations
            .AnyAsync(i => i.CycleId == cycle.Id && i.PersonId == personId && !i.Excluded, ct);
        if (!invited)
        {
            throw new NotInvitedToCycleException(cycle.Id, personId);
        }
    }

    // Every scored dimension must belong to the cycle's framework version, and no dimension may appear
    // twice in one payload. Caught here BEFORE the store writes, so an unknown dimension never hits the
    // FK (a 500) nor persists as a phantom score once a v2 framework introduces other dimension ids.
    private async Task ValidateDimensionsAsync(
        Guid frameworkVersionId, IReadOnlyList<SelfScoreInput> scores, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        foreach (var input in scores)
        {
            if (!seen.Add(input.DimensionId))
            {
                throw new SelfScoreDimensionException(
                    $"Dimension {input.DimensionId} appears more than once in the score payload.");
            }
        }

        var validDimensionIds = (await _db.Dimensions
            .Where(d => d.FrameworkVersionId == frameworkVersionId)
            .Select(d => d.Id)
            .ToListAsync(ct))
            .ToHashSet();

        // Explicit "is it present" test (not FirstOrDefault, whose Guid.Empty sentinel would let a
        // Guid.Empty dimension slip through) so every unknown id — including Guid.Empty — is rejected.
        foreach (var input in scores)
        {
            if (!validDimensionIds.Contains(input.DimensionId))
            {
                throw new SelfScoreDimensionException(
                    $"Dimension {input.DimensionId} is not part of the current cycle's framework version.");
            }
        }
    }

    private async Task<IReadOnlyList<SelfDimensionDescriptors>> DimensionsAsync(
        Guid frameworkVersionId, CancellationToken ct)
    {
        var dimensions = await _db.Dimensions
            .Where(d => d.FrameworkVersionId == frameworkVersionId)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => new { d.Id, d.Key, d.Name, d.DisplayOrder })
            .ToListAsync(ct);

        var dimensionIds = dimensions.Select(d => d.Id).ToList();
        var descriptors = await _db.LevelDescriptors
            .Where(ld => dimensionIds.Contains(ld.DimensionId))
            .Select(ld => new { ld.DimensionId, ld.Level, ld.LevelName, ld.DescriptorText })
            .ToListAsync(ct);

        return dimensions
            .Select(d => new SelfDimensionDescriptors(
                d.Id,
                d.Key,
                d.Name,
                d.DisplayOrder,
                descriptors
                    .Where(ld => ld.DimensionId == d.Id)
                    .OrderBy(ld => ld.Level)
                    .Select(ld => new SelfLevelDescriptor(ld.Level, ld.LevelName, ld.DescriptorText))
                    .ToList()))
            .ToList();
    }
}

/// <summary>Framework dimension + its 0–3 level descriptors, all from seeded data (FR-001).</summary>
internal sealed record SelfDimensionDescriptors(
    Guid Id, string Key, string Name, int DisplayOrder, IReadOnlyList<SelfLevelDescriptor> Levels);

/// <summary>One 0–3 descriptor: the level, its framework-wide name, and the dimension-specific text.</summary>
public sealed record SelfLevelDescriptor(int Level, string LevelName, string DescriptorText);

/// <summary>One dimension as presented on the self-assessment form: descriptors from data, the caller's
/// current self score/evidence (null when unscored this cycle), and the prior-cycle score for
/// pre-population (FR-062; null when none).</summary>
public sealed record SelfDimensionView(
    Guid DimensionId,
    string Key,
    string Name,
    int DisplayOrder,
    IReadOnlyList<SelfLevelDescriptor> Levels,
    int? SelfScore,
    string? SelfEvidence,
    int? PriorScore);

/// <summary>The whole self-assessment view for the current cycle. <see cref="Editable"/> is false when
/// the cycle no longer accepts this caller's writes (Closed without a late override) or the assessment
/// is already submitted — the client renders the form read-only up front rather than surfacing the lock
/// only on Save/Submit.</summary>
public sealed record SelfAssessmentView(
    Guid CycleId,
    string CycleName,
    string CycleState,
    bool Submitted,
    bool Editable,
    IReadOnlyList<SelfDimensionView> Dimensions);
