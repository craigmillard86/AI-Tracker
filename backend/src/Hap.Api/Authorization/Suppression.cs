using System.Text.Json.Serialization;

namespace Hap.Api.Authorization;

/// <summary>Why an aggregate was suppressed — carried on a <see cref="AggregateOutcome.Suppressed"/>
/// result so callers (and audit) know which rule fired, without exposing any count.</summary>
public enum SuppressionReason
{
    /// <summary>The node's own headcount is below the minimum group size (n &lt; 4).</summary>
    BelowThreshold,

    /// <summary>Publishing this node alongside its parent would leave an identifiable complement group
    /// of fewer than 4 people (the differencing / subtraction attack, FR-014 / SC-006).</summary>
    ComplementIdentifiable,
}

/// <summary>
/// The published form of an org-node aggregate — either a real figure or a suppressed placeholder.
/// A <see cref="Suppressed"/> result carries ONLY the reason and NEVER a numeric field (FR-071): the
/// type makes "a suppressed cell can never leak a number" a compile-time guarantee, not a
/// serialisation convention. See <see cref="SuppressedCell"/> for the wire shape.
/// </summary>
public abstract record AggregateOutcome
{
    private AggregateOutcome() { }

    /// <summary>A visible aggregate figure. Carries the numbers (n, mean, …) — only ever produced for
    /// a node that passed every suppression rule.</summary>
    public sealed record Published(int N, double MeanScore) : AggregateOutcome;

    /// <summary>A suppressed aggregate. No count, no mean — reason only (FR-071).</summary>
    public sealed record Suppressed(SuppressionReason Reason) : AggregateOutcome;

    public bool IsSuppressed => this is Suppressed;
}

/// <summary>
/// The wire shape of a suppressed aggregate cell (FR-071): renders as
/// <c>{"suppressed":true,"reason":"…"}</c> — never zero, never blank, and with NO numeric field that
/// could reveal the underlying group. This is the ONLY serialisable projection of a suppressed
/// result; a <see cref="AggregateOutcome.Published"/> serialises through its own DTO with the numbers.
/// </summary>
public sealed record SuppressedCell
{
    [JsonPropertyName("suppressed")]
    public bool Suppressed => true;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    public static SuppressedCell From(AggregateOutcome.Suppressed suppressed) =>
        new() { Reason = suppressed.Reason.ToString() };
}

/// <summary>One child node's headcount and mean, fed into a level evaluation.</summary>
public readonly record struct AggregateInput(Guid NodeId, int N, double MeanScore);

/// <summary>
/// Small-group + complement suppression (FR-014, research D2). Evaluated over the fixed rollup
/// hierarchy — v1 publishes only fixed-hierarchy rollups (no ad-hoc slicing), so the differencing
/// surface is exactly "parent vs. its published children".
///
/// <para>Two rules: (1) a node with its own n &lt; 4 is suppressed; (2) the set of PUBLISHED children
/// must leave an unpublished complement within the parent that is either empty or itself ≥ 4 — else a
/// &lt;4 group's aggregate is derivable by subtracting the published children from the parent. Rule 2
/// is enforced over the SET of siblings (not merely pairwise parent-vs-node), which closes the
/// multi-child differencing case — e.g. parent 11 with children 4, 4, 3: the 3 is suppressed by rule 1,
/// then one of the 4s is suppressed by rule 2 because 11 − 4 − 4 = 3 would otherwise be exposed.</para>
/// </summary>
public sealed class SuppressionEvaluator
{
    /// <summary>Minimum group size below which an aggregate is never published (spec §2, FR-014).</summary>
    public const int MinGroupSize = 4;

    /// <summary>
    /// Evaluate every child of a parent together, returning each child's outcome. This SET-BASED form is
    /// the only publication path (there is deliberately no per-node pairwise API: looping a pairwise
    /// "parent vs. this node" check per child re-opens the multi-child differencing leak — e.g. 11−4−4=3
    /// — that only a whole-level view can catch). Rule 1 suppresses any child with n &lt; 4; rule 2 then
    /// suppresses the smallest published child, repeatedly, until the unpublished complement
    /// (<paramref name="parentN"/> − Σ published children) is empty or ≥ 4. Already-suppressed siblings
    /// are excluded from the published set (research D2), so their members count toward the complement
    /// rather than being separately exposed.
    /// </summary>
    /// <exception cref="ArgumentException">If the children's headcounts sum to more than
    /// <paramref name="parentN"/> — a corrupt hierarchy (children cannot exceed their parent). Failing
    /// loudly here is a data-integrity tripwire: silently publishing on a negative complement could mask
    /// a suppression bypass.</exception>
    public IReadOnlyDictionary<Guid, AggregateOutcome> EvaluateLevel(
        int parentN, IReadOnlyList<AggregateInput> children)
    {
        long childTotal = children.Sum(c => (long)c.N);
        if (childTotal > parentN)
        {
            throw new ArgumentException(
                $"Corrupt hierarchy: children headcount {childTotal} exceeds parent {parentN} — a node's " +
                "children can never sum to more than the node itself.", nameof(children));
        }

        var outcomes = new Dictionary<Guid, AggregateOutcome>();
        var published = new List<AggregateInput>();

        foreach (var child in children)
        {
            if (child.N < MinGroupSize)
            {
                outcomes[child.NodeId] = new AggregateOutcome.Suppressed(SuppressionReason.BelowThreshold);
            }
            else
            {
                published.Add(child);
            }
        }

        // Rule 2: shrink the published set until the unpublished complement is safe (0 or ≥ 4).
        // Suppressing the smallest published child moves ≥ 4 members into the complement, so a
        // complement in 1..3 always clears in one step; the loop terminates as `published` strictly
        // shrinks each iteration.
        while (published.Count > 0)
        {
            int complement = parentN - published.Sum(c => c.N);
            if (complement <= 0 || complement >= MinGroupSize)
            {
                break;
            }
            var smallest = published.OrderBy(c => c.N).ThenBy(c => c.NodeId).First();
            published.Remove(smallest);
            outcomes[smallest.NodeId] = new AggregateOutcome.Suppressed(SuppressionReason.ComplementIdentifiable);
        }

        foreach (var child in published)
        {
            outcomes[child.NodeId] = new AggregateOutcome.Published(child.N, child.MeanScore);
        }

        return outcomes;
    }
}
