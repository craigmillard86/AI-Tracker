using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The sole implementation of <see cref="IAssessmentStore"/>/<see cref="ISelfAssessmentStore"/> and —
/// by construction and two architecture tests — the ONLY type in the codebase that queries the
/// <c>assessments</c>/<c>assessment_scores</c> tables. <c>HapDbContext</c> exposes no public
/// <c>DbSet&lt;Assessment&gt;</c>; every access here goes through <c>db.Set&lt;&gt;()</c>. Two guards
/// back this claim (research D1): <c>SeamBoundaryTests</c> is a source scan that fails the build if the
/// <c>Set&lt;&gt;()</c> query surface appears outside <c>Hap.Api/Authorization/**</c> (a regex over the
/// tree, not a type-system proof), and <c>SeamStoreImplementationTests</c> asserts by reflection that
/// this is the only production type implementing either storage port. Neither is a compiler guarantee;
/// together they make an out-of-seam query path a build failure rather than a silent leak.
/// </summary>
public sealed class SeamAssessmentStore : IAssessmentStore, ISelfAssessmentStore
{
    private readonly HapDbContext _db;

    public SeamAssessmentStore(HapDbContext db) => _db = db;

    private IQueryable<Assessment> Assessments => _db.Set<Assessment>();
    private IQueryable<AssessmentScore> Scores => _db.Set<AssessmentScore>();

    public async Task<IReadOnlyList<AssessmentScore>> GetIndividualScoresAsync(
        Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var assessmentId = await Assessments
            .Where(a => a.PersonId == subjectPersonId && a.CycleId == cycleId)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (assessmentId is null)
        {
            return Array.Empty<AssessmentScore>();
        }

        return await Scores.AsNoTracking()
            .Where(s => s.AssessmentId == assessmentId.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task<AssessmentWithScores?> GetAssessmentWithScoresAsync(
        Guid subjectPersonId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var assessment = await Assessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.PersonId == subjectPersonId && a.CycleId == cycleId, cancellationToken);
        if (assessment is null)
        {
            return null;
        }

        var scores = await Scores.AsNoTracking()
            .Where(s => s.AssessmentId == assessment.Id)
            .ToListAsync(cancellationToken);
        return new AssessmentWithScores(assessment, scores);
    }

    public async Task<AssessmentWithScores?> GetByIdWithScoresAsync(
        Guid assessmentId, CancellationToken cancellationToken = default)
    {
        var assessment = await Assessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);
        if (assessment is null)
        {
            return null;
        }

        var scores = await Scores.AsNoTracking()
            .Where(s => s.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);
        return new AssessmentWithScores(assessment, scores);
    }

    public async Task<IReadOnlyList<Assessment>> GetAssessmentsForPeopleAsync(
        Guid cycleId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        if (personIds.Count == 0)
        {
            return Array.Empty<Assessment>();
        }

        var ids = personIds as IReadOnlyList<Guid> ?? personIds.ToList();
        return await Assessments.AsNoTracking()
            .Where(a => a.CycleId == cycleId && ids.Contains(a.PersonId))
            .ToListAsync(cancellationToken);
    }

    public async Task ModerateAsync(
        Guid assessmentId,
        Guid moderatedByPersonId,
        IReadOnlyList<ManagerScoreInput> decisions,
        AuditLog auditRow,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var assessment = await Assessments
            .SingleOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new AssessmentNotFoundException(assessmentId);

        // State is checked BEFORE any score write so a wrong-state moderation fails 409 without partially
        // applying manager scores. Submitted is the normal path; AutoAdopted is the late-override path
        // (Q-017a × FR-068) — a post-close override reopens moderation, and by then close has auto-adopted a
        // placeholder that a real review replaces (the submission lock upstream has already required the
        // override for a closed cycle). Moderated (already resolved) / InProgress / NotStarted still fail.
        // Assessment.Moderate() re-asserts the same invariant as a backstop.
        if (assessment.State is not (AssessmentState.Submitted or AssessmentState.AutoAdopted))
        {
            throw new AssessmentStateException(assessment.Id, assessment.State, AssessmentState.Moderated);
        }

        var rows = await Scores
            .Where(s => s.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);
        var rowByDimension = rows.ToDictionary(s => s.DimensionId);

        foreach (var decision in decisions)
        {
            if (!rowByDimension.TryGetValue(decision.DimensionId, out var row))
            {
                throw new ModerationDimensionException(decision.DimensionId);
            }
            row.SetManager(decision.Score, decision.Comment); // FR-009 (comment@Δ≥2) + range → 422 in the seam
        }

        assessment.Moderate(moderatedByPersonId); // Submitted → Moderated; records moderator + instant
        _db.AuditLogs.Add(auditRow);               // FR-050 ScoreChange, staged in the SAME tx (fail-closed)

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another moderation of this assessment committed first (xmin moved) — the whole unit of work
            // (score writes + state transition + audit) rolls back, and the loser gets a clean 409.
            throw new ModerationConflictException(assessmentId);
        }
    }

    public async Task<AssessmentWithScores?> GetSelfAsync(
        Guid personId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var assessment = await Assessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.PersonId == personId && a.CycleId == cycleId, cancellationToken);
        if (assessment is null)
        {
            return null;
        }

        var scores = await Scores.AsNoTracking()
            .Where(s => s.AssessmentId == assessment.Id)
            .ToListAsync(cancellationToken);
        return new AssessmentWithScores(assessment, scores);
    }

    public async Task<IReadOnlyList<AssessmentScore>> GetSelfScoresForCycleAsync(
        Guid personId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var assessmentId = await Assessments.AsNoTracking()
            .Where(a => a.PersonId == personId && a.CycleId == cycleId)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (assessmentId is null)
        {
            return Array.Empty<AssessmentScore>();
        }

        return await Scores.AsNoTracking()
            .Where(s => s.AssessmentId == assessmentId.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertSelfScoresAsync(
        Guid personId, Guid cycleId, IReadOnlyList<SelfScoreInput> scores, CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var assessment = await Assessments
            .SingleOrDefaultAsync(a => a.PersonId == personId && a.CycleId == cycleId, cancellationToken);

        if (assessment is null)
        {
            assessment = Assessment.Start(cycleId, personId);
            _db.Add(assessment);
        }
        else if (assessment.State == AssessmentState.Submitted)
        {
            throw new AssessmentAlreadySubmittedException(personId, cycleId);
        }

        var existing = await Scores
            .Where(s => s.AssessmentId == assessment.Id)
            .ToListAsync(cancellationToken);

        foreach (var input in scores)
        {
            var row = existing.SingleOrDefault(s => s.DimensionId == input.DimensionId);
            if (row is null)
            {
                _db.Add(AssessmentScore.CreateSelf(assessment.Id, input.DimensionId, input.Score, input.Evidence));
            }
            else
            {
                row.SetSelf(input.Score, input.Evidence);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task SubmitSelfAsync(
        Guid personId, Guid cycleId, IReadOnlyCollection<Guid> requiredDimensionIds, CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var assessment = await Assessments
            .SingleOrDefaultAsync(a => a.PersonId == personId && a.CycleId == cycleId, cancellationToken);
        if (assessment is null)
        {
            // Nothing entered yet — treat as "incomplete" (0 of N) rather than a not-found, so the
            // caller gets the same 422 as a partially-filled assessment.
            throw new AssessmentIncompleteException(0, requiredDimensionIds.Count);
        }

        if (assessment.State == AssessmentState.Submitted)
        {
            throw new AssessmentAlreadySubmittedException(personId, cycleId);
        }

        var scoredDimensionIds = await Scores
            .Where(s => s.AssessmentId == assessment.Id)
            .Select(s => s.DimensionId)
            .ToListAsync(cancellationToken);

        var scoredSet = scoredDimensionIds.ToHashSet();
        var scoredRequired = requiredDimensionIds.Count(scoredSet.Contains);
        if (scoredRequired < requiredDimensionIds.Count)
        {
            throw new AssessmentIncompleteException(scoredRequired, requiredDimensionIds.Count);
        }

        assessment.Submit(); // InProgress → Submitted (throws AssessmentStateException if not InProgress)
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent submit of the same assessment won the race (xmin moved) — the loser's write
            // rolls back and is reported as an already-submitted conflict (409), the same status the
            // serialized double-submit path returns, so the race never surfaces a 500 (HAP-8 invariant).
            throw new AssessmentAlreadySubmittedException(personId, cycleId);
        }
    }
}
