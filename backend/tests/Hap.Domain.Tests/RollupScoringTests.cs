using Hap.Domain.Scoring;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>
/// HAP-10 pure scoring maths (FR-015 mean, FR-016 floor, FR-068 rollup body). Tagged
/// <c>Category=PrivacyReporting</c>: these figures feed every leadership rollup and must reconcile, so
/// they run in the always-on regression suite. Storage, org walk, and suppression are exercised by the
/// integration tests; this pins the arithmetic.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public class RollupScoringTests
{
    // --- primitives ------------------------------------------------------------------------------

    [Fact]
    public void Mean_is_the_arithmetic_mean_to_two_decimal_places()
    {
        // (3 + 2 + 2 + 0 + 1 + 2 + 3) / 7 = 13/7 = 1.857… → 1.86
        Assert.Equal(1.86, MaturityScoring.Mean(new[] { 3, 2, 2, 0, 1, 2, 3 }));
        Assert.Equal(2.00, MaturityScoring.Mean(new[] { 2, 2, 2 }));
        Assert.Equal(1.50, MaturityScoring.Mean(new[] { 1, 2 }));
    }

    [Fact]
    public void Mean_rounds_half_away_from_zero_deterministically()
    {
        // 1/8 = 0.125 → 0.13 (away from zero, not banker's-rounding 0.12)
        Assert.Equal(0.13, MaturityScoring.Mean(new[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void Mean_throws_on_an_empty_set() =>
        Assert.Throws<ArgumentException>(() => MaturityScoring.Mean(Array.Empty<int>()));

    [Fact]
    public void FloorLevel_is_the_minimum_dimension_score()
    {
        Assert.Equal(0, MaturityScoring.FloorLevel(new[] { 3, 2, 2, 0, 3, 3, 3 }));
        Assert.Equal(2, MaturityScoring.FloorLevel(new[] { 2, 2, 3, 2, 2, 2, 3 })); // "level 2 only when all ≥2"
    }

    [Fact]
    public void FloorLevel_throws_on_an_empty_set() =>
        Assert.Throws<ArgumentException>(() => MaturityScoring.FloorLevel(Array.Empty<int>()));

    [Fact]
    public void Property_floor_is_at_or_below_every_dimension_score_and_the_mean()
    {
        var rng = new Random(20260722); // seeded — deterministic property sweep, no external dep
        for (var i = 0; i < 5000; i++)
        {
            var count = rng.Next(1, 8); // 1..7 dimensions
            var scores = Enumerable.Range(0, count).Select(_ => rng.Next(0, 4)).ToArray();

            var floor = MaturityScoring.FloorLevel(scores);
            var mean = MaturityScoring.Mean(scores);

            Assert.All(scores, s => Assert.True(floor <= s));
            Assert.True(floor <= mean, $"floor {floor} must be ≤ mean {mean} for [{string.Join(",", scores)}]");
        }
    }

    // --- node rollup body ------------------------------------------------------------------------

    private static PersonRollupInput Person(
        string id, bool countsTowardCompletion, bool autoAdopted, params (int self, int manager)[] dims) =>
        new(
            DeterministicGuid(id),
            countsTowardCompletion,
            dims.Select((d, i) => new DimensionDatum($"D{i}", d.self, d.manager)).ToList(),
            autoAdopted);

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = new byte[16];
        var s = System.Text.Encoding.UTF8.GetBytes(seed);
        Array.Copy(s, bytes, Math.Min(s.Length, 16));
        return new Guid(bytes);
    }

    [Fact]
    public void Compute_reports_per_dimension_mean_over_the_scored_population()
    {
        var rollup = RollupComputation.Compute(new[]
        {
            Person("a", true, false, (2, 2), (3, 3)),
            Person("b", true, false, (1, 1), (2, 2)),
            Person("c", true, false, (0, 0), (1, 1)),
        });

        Assert.Equal(3, rollup.N);
        Assert.Equal(1.00, rollup.PerDimensionMean["D0"]); // (2+1+0)/3
        Assert.Equal(2.00, rollup.PerDimensionMean["D1"]); // (3+2+1)/3
    }

    [Fact]
    public void Compute_floor_distribution_counts_people_by_their_weakest_dimension()
    {
        var rollup = RollupComputation.Compute(new[]
        {
            Person("a", true, false, (2, 2), (3, 3)), // floor 2
            Person("b", true, false, (2, 2), (2, 2)), // floor 2
            Person("c", true, false, (0, 0), (3, 3)), // floor 0
        });

        Assert.Equal(2, rollup.FloorLevelDistribution[2]);
        Assert.Equal(1, rollup.FloorLevelDistribution[0]);
        Assert.False(rollup.FloorLevelDistribution.ContainsKey(1));
    }

    [Fact]
    public void Compute_completion_uses_a_separate_base_that_excludes_a_non_completing_person()
    {
        var rollup = RollupComputation.Compute(new[]
        {
            Person("a", true, false, (2, 2)),                     // active + scored
            Person("b", true, false, (1, 1)),                     // active + scored
            new PersonRollupInput(DeterministicGuid("c"), true, ScoreSet: null, AutoAdopted: false), // invited, did not submit
        });

        Assert.Equal(2, rollup.N);                       // scored population
        Assert.Equal(3, rollup.CompletionDenominator);   // three invited-active
        Assert.Equal(2d / 3d, rollup.CompletionPct);
    }

    [Fact]
    public void Compute_scored_population_includes_a_leaver_who_is_out_of_the_completion_base()
    {
        // The FR-024/§3.5 split: a submitted mid-cycle leaver (CountsTowardCompletion=false) still
        // contributes to means/floor/N, but is out of the completion denominator. scored-n ≠ completion-n.
        var rollup = RollupComputation.Compute(new[]
        {
            Person("active", true, false, (2, 2)),
            Person("leaver", false, false, (0, 0)), // left before close, but had submitted
        });

        Assert.Equal(2, rollup.N);                     // both scored
        Assert.Equal(1, rollup.CompletionDenominator); // only the active one
        Assert.Equal(1.00, rollup.CompletionPct);      // 1 of 1 active submitted
        Assert.Equal(1.00, rollup.PerDimensionMean["D0"]); // (2+0)/2 — leaver still in the mean
        Assert.Equal(1, rollup.FloorLevelDistribution[0]); // leaver's floor counted
        Assert.Equal(1, rollup.FloorLevelDistribution[2]);
    }

    [Fact]
    public void Compute_unmoderated_pct_is_auto_adopted_over_the_scored_population()
    {
        var rollup = RollupComputation.Compute(new[]
        {
            Person("a", true, autoAdopted: true, (2, 2)),
            Person("b", true, autoAdopted: false, (2, 2)),
            Person("c", true, autoAdopted: false, (2, 2)),
            Person("d", true, autoAdopted: true, (2, 2)),
        });

        Assert.Equal(0.5, rollup.UnmoderatedPct);
    }

    [Fact]
    public void Compute_calibration_delta_is_identical_with_or_without_an_auto_adopted_member()
    {
        // FR-068: auto-adopted rows are excluded from the calibration delta (their |self−manager| is 0).
        var moderatedOnly = new[]
        {
            Person("a", true, false, (3, 1)), // |Δ| = 2
            Person("b", true, false, (2, 2)), // |Δ| = 0
        };
        var withAutoAdopted = moderatedOnly.Append(
            Person("c", true, autoAdopted: true, (0, 0))).ToArray(); // auto-adopted, Δ 0 — must not shift the mean

        var a = RollupComputation.Compute(moderatedOnly).CalibrationDelta;
        var b = RollupComputation.Compute(withAutoAdopted).CalibrationDelta;

        Assert.Equal(1.00, a["D0"]); // (2 + 0) / 2 moderated-only
        Assert.Equal(a["D0"], b["D0"]); // identical with the auto-adopted member added
    }

    [Fact]
    public void Compute_calibration_delta_is_empty_when_every_scored_row_was_auto_adopted()
    {
        var rollup = RollupComputation.Compute(new[]
        {
            Person("a", true, autoAdopted: true, (2, 2)),
            Person("b", true, autoAdopted: true, (1, 1)),
        });

        Assert.Empty(rollup.CalibrationDelta); // no calibration signal, never a dict of zeros
        Assert.Equal(1.00, rollup.UnmoderatedPct);
    }
}
