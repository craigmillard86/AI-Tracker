namespace Hap.Domain.Scoring;

/// <summary>One dimension's pair on a person's assessment as the rollup sees it: the retained self score
/// and the moderated score of record (<see cref="Manager"/> — the manager score, or the self score copied
/// on auto-adoption, FR-068). Both 0–3.</summary>
public readonly record struct DimensionDatum(string DimensionKey, int Self, int Manager);

/// <summary>
/// One person's contribution to a node aggregate. <see cref="ScoreSet"/> is null when the person did NOT
/// submit (no moderated/auto-adopted record) — they still count toward <see cref="CountsTowardCompletion"/>
/// if active+invited, but not toward the scored population. A non-null <see cref="ScoreSet"/> means they
/// are in the scored population (their means/floor count) even if they have since left
/// (<see cref="CountsTowardCompletion"/> false) — the FR-024/§3.5 split.
/// </summary>
public sealed record PersonRollupInput(
    Guid PersonId,
    bool CountsTowardCompletion,
    IReadOnlyList<DimensionDatum>? ScoreSet,
    bool AutoAdopted);

/// <summary>The frozen figures for one node, computed purely from its <see cref="PersonRollupInput"/> set
/// (no storage, no org walk). Mirrors the numeric fields of <c>Hap.Domain.Rollups.RollupSnapshot</c>.</summary>
public sealed record NodeRollup(
    int N,
    IReadOnlyDictionary<string, double> PerDimensionMean,
    IReadOnlyDictionary<int, int> FloorLevelDistribution,
    int CompletionDenominator,
    double CompletionPct,
    double UnmoderatedPct,
    IReadOnlyDictionary<string, double> CalibrationDelta);

/// <summary>
/// Aggregates a node's people into the numeric body of a rollup snapshot (FR-015/016/019/068). PURE and
/// deterministic — the reconciliation guard (Art. VI.4) recomputes every figure this way from raw rows and
/// asserts equality with what was stored, so this is the single definition of "the rollup maths". Keeps the
/// two populations strictly apart (class doc on <c>RollupSnapshot</c>): the scored population drives means,
/// floor distribution and unmoderated %, while a separate completion base drives completion % — a
/// mid-cycle leaver is in the former and out of the latter.
/// </summary>
public static class RollupComputation
{
    public static NodeRollup Compute(IReadOnlyCollection<PersonRollupInput> people)
    {
        var scored = people.Where(p => p.ScoreSet is { Count: > 0 }).ToList();
        var n = scored.Count;

        var completionDenominator = people.Count(p => p.CountsTowardCompletion);
        var completionNumerator = people.Count(p => p.CountsTowardCompletion && p.ScoreSet is { Count: > 0 });
        var completionPct = completionDenominator == 0
            ? 0d
            : completionNumerator / (double)completionDenominator;

        var unmoderatedN = scored.Count(p => p.AutoAdopted);
        var unmoderatedPct = n == 0 ? 0d : unmoderatedN / (double)n;

        // Per-dimension mean of the score of record over the WHOLE scored population (auto-adopted
        // included — FR-068: "unmoderated assessments remain in all rollups").
        var perDimensionMean = new Dictionary<string, double>();
        foreach (var group in scored
                     .SelectMany(p => p.ScoreSet!)
                     .GroupBy(d => d.DimensionKey)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            perDimensionMean[group.Key] = MaturityScoring.Mean(group.Select(d => d.Manager).ToList());
        }

        // Floor distribution: each scored person's floor (min score of record across their dimensions).
        var floorDistribution = new Dictionary<int, int>();
        foreach (var person in scored)
        {
            var floor = MaturityScoring.FloorLevel(person.ScoreSet!.Select(d => d.Manager).ToList());
            floorDistribution[floor] = floorDistribution.GetValueOrDefault(floor) + 1;
        }

        // Calibration delta: mean |self − manager| per dimension, EXCLUDING auto-adopted rows (their delta
        // is definitionally zero and would deflate the signal — FR-068). Empty when every scored row was
        // auto-adopted (no calibration signal exists), never a dictionary of zeros.
        var calibrationDelta = new Dictionary<string, double>();
        var moderatedOnly = scored.Where(p => !p.AutoAdopted).ToList();
        foreach (var group in moderatedOnly
                     .SelectMany(p => p.ScoreSet!)
                     .GroupBy(d => d.DimensionKey)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            calibrationDelta[group.Key] = MaturityScoring.Mean(
                group.Select(d => Math.Abs(d.Self - d.Manager)).ToList());
        }

        return new NodeRollup(
            n, perDimensionMean, floorDistribution,
            completionDenominator, completionPct, unmoderatedPct, calibrationDelta);
    }
}
