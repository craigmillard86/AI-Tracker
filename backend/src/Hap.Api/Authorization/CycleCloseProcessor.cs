using Hap.Domain.Assessments;
using Hap.Domain.Rollups;
using Hap.Infrastructure;
using Hap.Infrastructure.Cycles;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The seam-resident implementation of <see cref="ICycleCloseProcessor"/> (HAP-10). It is IN the
/// visibility seam because it reads moderated maturity scores, which no code outside
/// <c>Hap.Api/Authorization</c> may touch (research D1). Hooked into <see cref="CycleService.CloseAsync"/>
/// and run inside that method's transaction — it stages every write on the shared <see cref="HapDbContext"/>
/// and never opens its own transaction or commits.
///
/// <para>In one pass it: (1) auto-adopts every Submitted-but-unmoderated assessment — the self score
/// becomes the score of record, flagged unmoderated (FR-068), Moderated ones untouched, Q-020 handled
/// with no senior-leader special case; (2) delegates to the shared <see cref="RollupPipeline"/> to build a
/// <see cref="RollupSnapshot"/> for every Team/BU/Group/Portfolio/AllHig node — the SAME pipeline the live
/// open-cycle dashboard uses (HAP-11), so a just-closed cycle's snapshots equal its live figures by
/// construction; (3) persists the frozen snapshots (append-only). Because auto-adoption runs BEFORE the
/// pipeline reads scores, the scored population correctly includes the newly auto-adopted rows.</para>
/// </summary>
public sealed class CycleCloseProcessor : ICycleCloseProcessor
{
    private readonly HapDbContext _db;
    private readonly OrgGraphLoader _graphLoader;
    private readonly RollupPipeline _pipeline;

    public CycleCloseProcessor(HapDbContext db, OrgGraphLoader graphLoader, RollupPipeline pipeline)
    {
        _db = db;
        _graphLoader = graphLoader;
        _pipeline = pipeline;
    }

    public async Task RunAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        var cycle = await _db.Cycles.FindAsync(new object[] { cycleId }, cancellationToken)
            ?? throw new InvalidOperationException($"Cycle {cycleId} not found during close processing.");

        // --- (1) auto-adoption: Submitted-but-unmoderated → AutoAdopted, self copied to the score of record.
        // Tracked loads so the mutations below are visible to the pipeline's subsequent re-read (identity
        // resolution), and commit together with the snapshots when CloseAsync saves.
        var assessments = await _db.Set<Assessment>()
            .Where(a => a.CycleId == cycleId && a.State == AssessmentState.Submitted)
            .ToListAsync(cancellationToken);
        if (assessments.Count > 0)
        {
            var ids = assessments.Select(a => a.Id).ToList();
            var scores = await _db.Set<AssessmentScore>()
                .Where(s => ids.Contains(s.AssessmentId))
                .ToListAsync(cancellationToken);
            var scoresByAssessment = scores.GroupBy(s => s.AssessmentId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var assessment in assessments)
            {
                if (scoresByAssessment.TryGetValue(assessment.Id, out var rows))
                {
                    foreach (var row in rows)
                    {
                        row.AdoptSelf(); // manager := self, no comment (Δ0) — FR-068
                    }
                }
                assessment.AutoAdopt(); // Submitted → AutoAdopted, unmoderated = true
            }
        }

        // --- (2) build the frozen rollups via the shared pipeline (auto-adopted rows now count) ------------
        var graph = await _graphLoader.LoadAsync(cancellationToken);
        var personInputs = await _pipeline.BuildPersonInputsAsync(_db, graph, cycle, tracked: true, cancellationToken);
        var nodes = _pipeline.ComputeNodes(graph, personInputs);

        // --- (3) persist snapshots (append-only; CloseAsync commits) ---------------------------------------
        foreach (var node in nodes)
        {
            var r = node.Rollup;
            _db.RollupSnapshots.Add(RollupSnapshot.Create(
                cycleId,
                node.Type,
                node.Ref,
                r.N,
                r.PerDimensionMean,
                r.FloorLevelDistribution,
                r.CompletionDenominator,
                r.CompletionPct,
                r.UnmoderatedPct,
                r.CalibrationDelta,
                node.Suppressed,
                node.SuppressionReason));
        }
    }
}
