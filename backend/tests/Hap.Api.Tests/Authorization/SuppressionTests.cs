using System.Text.Json;
using Hap.Api.Authorization;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// Exhaustive coverage of <see cref="SuppressionEvaluator"/> (FR-014 / research D2 / SC-006) against the
/// synthetic generator's engineered edge cases, the multi-child differencing case, the corrupt-input
/// tripwire, and the FR-071 "suppressed cells never leak a number" serialisation guarantee. Everything
/// goes through the SET-BASED <c>EvaluateLevel</c> — the only publication path (there is no per-node
/// pairwise API to misuse). Category=PrivacyReporting.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class SuppressionTests
{
    private static readonly SuppressionEvaluator Eval = new();

    private static AggregateInput Node(int n, double mean = 1.0) => new(Guid.NewGuid(), n, mean);

    private static AggregateOutcome Only(int parentN, AggregateInput child) =>
        Eval.EvaluateLevel(parentN, new[] { child })[child.NodeId];

    // --- the engineered synth edge cases --------------------------------------------------------

    [Fact]
    public void Team_of_three_is_suppressed_below_threshold()
    {
        // edge (a): a team of exactly 3 within a large BU.
        AssertSuppressed(Only(50, Node(3)), SuppressionReason.BelowThreshold);
    }

    [Fact]
    public void Sub_four_bu_is_suppressed_below_threshold()
    {
        // edge (b): a whole BU of 3 people (BU Lead + 2) inside a large group.
        AssertSuppressed(Only(500, Node(3)), SuppressionReason.BelowThreshold);
    }

    [Fact]
    public void Single_team_bu_child_is_suppressed_by_complement()
    {
        // edge (c): a BU of 8 that is a single team of 7 (+ BU Lead). The complement of 1 is identifiable.
        AssertSuppressed(Only(8, Node(7)), SuppressionReason.ComplementIdentifiable);
    }

    [Fact]
    public void Team_of_four_in_org_of_seven_is_suppressed_by_complement()
    {
        // edge (d): org of 7 with a team of 4 → complement of 3 is identifiable → suppress the team.
        AssertSuppressed(Only(7, Node(4)), SuppressionReason.ComplementIdentifiable);
    }

    [Fact]
    public void Balanced_four_four_four_bu_publishes_every_team()
    {
        // A BU of 12 with three teams of 4: each complement is 8 (≥4) → all published.
        var outcomes = Eval.EvaluateLevel(12, new[] { Node(4, 1.0), Node(4, 2.0), Node(4, 3.0) });
        Assert.All(outcomes.Values, o => Assert.IsType<AggregateOutcome.Published>(o));
    }

    // --- multi-child differencing (stronger than a pairwise rule) -------------------------------

    [Fact]
    public void Defeats_multi_child_differencing()
    {
        // parent 11 with children 4, 4, 3. The 3 is below threshold. Publishing BOTH 4s would expose
        // 11 − 4 − 4 = 3 by subtraction, so exactly one 4 must also be suppressed (complement).
        var a = Node(4, 1.0);
        var c = Node(4, 2.0);
        var small = Node(3, 3.0);

        var outcomes = Eval.EvaluateLevel(11, new[] { a, c, small });

        AssertSuppressed(outcomes[small.NodeId], SuppressionReason.BelowThreshold);
        Assert.Equal(1, outcomes.Values.Count(o => o is AggregateOutcome.Published));
        Assert.Equal(1, outcomes.Values.Count(o =>
            o is AggregateOutcome.Suppressed s && s.Reason == SuppressionReason.ComplementIdentifiable));
    }

    [Fact]
    public void Published_node_carries_its_numbers()
    {
        var child = Node(6, 2.5);
        var published = Assert.IsType<AggregateOutcome.Published>(Only(20, child));
        Assert.Equal(6, published.N);
        Assert.Equal(2.5, published.MeanScore);
    }

    // --- corrupt-input tripwire -----------------------------------------------------------------

    [Fact]
    public void Children_exceeding_the_parent_throw_a_data_integrity_error()
    {
        // 4 + 4 = 8 > parent 7 — a corrupt hierarchy. Fail loudly rather than silently publish.
        Assert.Throws<ArgumentException>(() => Eval.EvaluateLevel(7, new[] { Node(4), Node(4) }));
    }

    // --- FR-071: a suppressed cell serialises with NO numeric field -----------------------------

    [Fact]
    public void Suppressed_cell_serialises_to_flag_and_reason_only_no_numbers()
    {
        var cell = SuppressedCell.From(new AggregateOutcome.Suppressed(SuppressionReason.ComplementIdentifiable));
        var json = JsonSerializer.Serialize(cell);

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "reason", "suppressed" }, props);
        Assert.True(doc.RootElement.GetProperty("suppressed").GetBoolean());
        Assert.Equal("ComplementIdentifiable", doc.RootElement.GetProperty("reason").GetString());

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            Assert.NotEqual(JsonValueKind.Number, prop.Value.ValueKind);
        }
    }

    [Fact]
    public void Suppressed_cell_never_renders_as_zero_or_blank()
    {
        var json = JsonSerializer.Serialize(SuppressedCell.From(
            new AggregateOutcome.Suppressed(SuppressionReason.BelowThreshold)));
        Assert.Contains("\"suppressed\":true", json);
        Assert.DoesNotContain("\"n\"", json);
        Assert.DoesNotContain("\"mean\"", json);
    }

    // === QA ADVERSARIAL (hap-qa) — boundary-value + differencing attempts ==========================
    // §9.3(b): try to recover a <4 figure by manipulating the exact 3/4 boundary and by composing
    // multiple simultaneous below-threshold suppressions in one level. All attempts below stay
    // suppressed/safe — no recoverable <4 figure found.

    [Fact]
    public void Complement_of_exactly_four_is_the_safe_boundary_and_publishes()
    {
        // ATTACK: probe the exact edge of rule 2. parent=8, child=4 -> complement=4 (== MinGroupSize,
        // the boundary between "identifiable" and "safe"). Must publish, not suppress.
        AssertPublished(Only(8, Node(4)));
    }

    [Fact]
    public void Complement_of_exactly_three_is_the_unsafe_boundary_and_suppresses()
    {
        // ATTACK: one below the boundary. parent=7, child=4 -> complement=3 -> must suppress (mirrors
        // Team_of_four_in_org_of_seven_is_suppressed_by_complement but stated as an explicit boundary
        // pair with the n=4-complement case above, proving the flip happens at exactly 4 vs 3).
        AssertSuppressed(Only(7, Node(4)), SuppressionReason.ComplementIdentifiable);
    }

    [Fact]
    public void Own_headcount_boundary_three_suppresses_four_publishes()
    {
        // ATTACK: probe rule 1's exact edge in isolation (large parent, so rule 2 never fires).
        AssertSuppressed(Only(1000, Node(3)), SuppressionReason.BelowThreshold);
        AssertPublished(Only(1000, Node(4)));
    }

    [Fact]
    public void Multiple_simultaneous_below_threshold_children_still_yield_a_correct_complement()
    {
        // ATTACK: parent 15 with TWO below-threshold children (3, 3) and one large one (9). Rule 1
        // suppresses both 3s; the remaining complement is 15 - 9 = 6 (>= 4, safe), so the 9 publishes.
        // If the evaluator naively recomputed complement against ONLY the first suppression, or treated
        // the two below-threshold children as one, this could mis-publish 9 (masking a leak of the
        // combined 6) — verify it doesn't.
        var a = Node(3, 1.0);
        var c = Node(3, 2.0);
        var big = Node(9, 3.0);

        var outcomes = Eval.EvaluateLevel(15, new[] { a, c, big });

        AssertSuppressed(outcomes[a.NodeId], SuppressionReason.BelowThreshold);
        AssertSuppressed(outcomes[c.NodeId], SuppressionReason.BelowThreshold);
        AssertPublished(outcomes[big.NodeId]);
    }

    [Fact]
    public void Repeated_identical_queries_leak_nothing_beyond_the_first_answer()
    {
        // ATTACK: differencing-by-repetition — call the SAME level evaluation twice and confirm it is
        // pure/deterministic (no hidden state, no randomised jitter that could be averaged away to
        // recover a suppressed figure across repeated calls).
        var children = new[] { Node(4, 1.0), Node(4, 2.0), Node(3, 3.0) };
        var first = Eval.EvaluateLevel(11, children);
        var second = Eval.EvaluateLevel(11, children);

        Assert.Equal(first.Count, second.Count);
        foreach (var (id, outcome) in first)
        {
            Assert.Equal(outcome, second[id]); // records compare by value — identical outcome, every time
        }
    }

    private static void AssertPublished(AggregateOutcome outcome) =>
        Assert.IsType<AggregateOutcome.Published>(outcome);

    private static void AssertSuppressed(AggregateOutcome outcome, SuppressionReason expected)
    {
        var suppressed = Assert.IsType<AggregateOutcome.Suppressed>(outcome);
        Assert.Equal(expected, suppressed.Reason);
        Assert.True(outcome.IsSuppressed);
    }
}
