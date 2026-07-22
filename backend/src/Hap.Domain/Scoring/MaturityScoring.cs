namespace Hap.Domain.Scoring;

/// <summary>
/// The two pure maturity-scoring primitives (FR-015 mean, FR-016 floor), kept free of any storage or
/// org concern so they are exhaustively property-testable. <b>Mean</b> is the continuous trend metric —
/// the arithmetic mean of a set of 0–3 scores to 2dp. <b>Floor</b> is the level label — the weakest
/// dimension (minimum), so an individual/team is "level 2" only when EVERY dimension is ≥2 (root spec
/// §9 "weakest-dimension floor"). Both are used at two granularities by the rollup: a person's mean/floor
/// across their 7 dimensions, and a dimension's mean across a node's people.
/// </summary>
public static class MaturityScoring
{
    /// <summary>The number of decimal places means are reported to (FR-015; data-model.md 2dp).</summary>
    public const int MeanDecimals = 2;

    /// <summary>Arithmetic mean of <paramref name="scores"/>, rounded to <see cref="MeanDecimals"/> dp
    /// (away-from-zero, so 0.005 → 0.01 deterministically). Throws on an empty set — a mean of nothing is
    /// undefined, and the caller must decide (a node with no scored population produces no mean entry,
    /// never a silent 0).</summary>
    public static double Mean(IReadOnlyCollection<int> scores)
    {
        if (scores.Count == 0)
        {
            throw new ArgumentException("Mean is undefined for an empty score set.", nameof(scores));
        }

        var mean = scores.Sum() / (double)scores.Count;
        return Math.Round(mean, MeanDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Floor level = the minimum score across <paramref name="dimensionScores"/> (FR-016). Throws
    /// on an empty set — an individual's floor is undefined without at least one dimension.</summary>
    public static int FloorLevel(IReadOnlyCollection<int> dimensionScores)
    {
        if (dimensionScores.Count == 0)
        {
            throw new ArgumentException("Floor level is undefined for an empty dimension set.", nameof(dimensionScores));
        }

        return dimensionScores.Min();
    }
}
