using System.Numerics;
using Hap.Api.Authorization;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// The hierarchy-global differencing guarantee (HAP-11 BR1, FR-014/SC-006, research D2). Every case asserts
/// the PROPERTY the red-team attacks — after <see cref="HierarchySuppression.Close"/>, no suppressed node's
/// count is recoverable by summing published nodes through the tree identities — using an independent
/// determinability check, not the algorithm's own internals. Category=PrivacyReporting.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class HierarchySuppressionTests
{
    /// <summary>Independent (test-owned) check: the set of node indices whose count is FORCED by the published
    /// nodes via <c>parent = Σ children + slack</c>, with teamless slack modelled as an unknown. For a laminar
    /// tree the one-unknown fixpoint is complete, so a node in this set is genuinely recoverable.</summary>
    private static HashSet<int> DeterminedNodes(int n, int[] parent, long[] count, bool[] published)
    {
        var children = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            children[i] = new List<int>();
        }
        for (var i = 0; i < n; i++)
        {
            if (parent[i] >= 0)
            {
                children[parent[i]].Add(i);
            }
        }

        // Equations as variable-id lists; phantom slack vars get ids >= n.
        var equations = new List<List<int>>();
        var phantom = n;
        for (var i = 0; i < n; i++)
        {
            if (children[i].Count == 0)
            {
                continue;
            }
            long s = count[i];
            foreach (var c in children[i])
            {
                s -= count[c];
            }
            var vars = new List<int> { i };
            vars.AddRange(children[i]);
            if (s > 0)
            {
                vars.Add(phantom++);
            }
            equations.Add(vars);
        }

        var known = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (published[i])
            {
                known.Add(i);
            }
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var vars in equations)
            {
                var unknowns = vars.Where(v => !known.Contains(v)).ToList();
                if (unknowns.Count == 1 && known.Add(unknowns[0]))
                {
                    changed = true;
                }
            }
        }
        while (changed);

        known.RemoveWhere(v => v >= n); // drop phantoms — only real nodes are protected
        return known;
    }

    private static void AssertNoSuppressedNodeIsRecoverable(int n, int[] parent, long[] count, bool[] closed)
    {
        var published = new bool[n];
        for (var i = 0; i < n; i++)
        {
            published[i] = !closed[i];
        }
        var determined = DeterminedNodes(n, parent, count, published);
        for (var i = 0; i < n; i++)
        {
            if (closed[i])
            {
                Assert.False(determined.Contains(i), $"suppressed node {i} (n={count[i]}) is recoverable by differencing");
            }
        }
    }

    [Fact]
    public void Executive_cannot_recover_a_suppressed_sub4_branch_by_differencing()
    {
        // Red-team BR1 repro. Indices: 0 AllHig(14) 1 P1(11) 2 P2(3) 3 GA(11) 4 GB(3) 5 BU_A(7) 6 BU_B(4)
        // 7 BU_C(3) 8 MGR_A(4) 9 MGR_A2(3) 10 MGR_B(4) 11 HEAD_C-team(3).
        var parent = new[] { -1, 0, 0, 1, 2, 3, 3, 4, 5, 5, 6, 7 };
        var count = new long[] { 14, 11, 3, 11, 3, 7, 4, 3, 4, 3, 4, 3 };
        // Per-parent verdict (what the old code froze): P2,GB,BU_C,HEAD_C team = N<4; P1 = complement;
        // MGR_A2 = N<4; MGR_A = complement. Published: AllHig, GA, BU_A, BU_B, MGR_B.
        var initial = new bool[12];
        foreach (var s in new[] { 1, 2, 4, 7, 9, 8, 11 })
        {
            initial[s] = true;
        }

        var closed = HierarchySuppression.Close(12, parent, count, initial);

        // The attack figure — AllHig(14) − GA(11) = 3, or AllHig − BU_A − BU_B = 3 — must no longer stand.
        AssertNoSuppressedNodeIsRecoverable(12, parent, count, closed);
        // Everything already suppressed stays suppressed (monotonic).
        for (var i = 0; i < 12; i++)
        {
            if (initial[i])
            {
                Assert.True(closed[i]);
            }
        }
        // The useful top total is preserved (all-HIG is not needlessly hidden).
        Assert.False(closed[0]);
    }

    [Fact]
    public void A_clean_wide_tree_is_not_over_suppressed()
    {
        // AllHig(12) → GA(8){BU_A(4),BU_B(4)}, GB(4){BU_C(4)}; teams all 4. Everything ≥4, no leak.
        var parent = new[] { -1, 0, 0, 1, 1, 2, 3, 4, 5 };
        var count = new long[] { 12, 8, 4, 4, 4, 4, 4, 4, 4 };
        var initial = new bool[9]; // nothing suppressed by the per-parent pass

        var closed = HierarchySuppression.Close(9, parent, count, initial);

        for (var i = 0; i < 9; i++)
        {
            Assert.False(closed[i]); // no over-suppression on a clean tree
        }
    }

    [Fact]
    public void A_deep_single_child_chain_collapses_as_one_membership_class()
    {
        // AllHig(10) → P(10) → G(10) → BU(10) → team(6) + teamless(4). A(0),P(1),G(2),BU(3),team(4).
        // Suppose team is suppressed (say complement in some setup); the equal-membership chain A=P=G=BU
        // must not let the published upper nodes reveal a suppressed lower one. Here nothing is < 4 so the
        // chain is published; make BU suppressed artificially and assert the chain collapses.
        var parent = new[] { -1, 0, 1, 2, 3 };
        var count = new long[] { 10, 10, 10, 10, 6 };
        var initial = new bool[5];
        initial[3] = true; // BU suppressed

        var closed = HierarchySuppression.Close(5, parent, count, initial);

        // BU (10) equals P and G and AllHig (single-child, no slack) — publishing any of them reveals BU,
        // so the whole equal-membership chain must be suppressed.
        Assert.True(closed[0] && closed[1] && closed[2] && closed[3]);
        AssertNoSuppressedNodeIsRecoverable(5, parent, count, closed);
    }

    [Fact]
    public void Multi_child_differencing_is_closed()
    {
        // Parent(20) with children A(10),B(7),C(3). C<4 suppressed; the published complement 20−10−7 = 3
        // recovers C, so one more must be suppressed. Parent published.
        var parent = new[] { -1, 0, 0, 0 };
        var count = new long[] { 20, 10, 7, 3 };
        var initial = new bool[4];
        initial[3] = true; // C = N<4

        var closed = HierarchySuppression.Close(4, parent, count, initial);

        AssertNoSuppressedNodeIsRecoverable(4, parent, count, closed);
        Assert.True(closed[3]);
    }

    [Fact]
    public void Teamless_slack_absorbs_the_complement_so_no_extra_suppression_is_needed()
    {
        // BU(11) with team A(4), team B(4) and 3 teamless. A published, B published: BU − A − B = 3 is the
        // teamless slack (an unknown phantom, not a protected node), so neither team is recoverable → no
        // suppression needed beyond the per-parent pass.
        var parent = new[] { -1, 0, 0 };
        var count = new long[] { 11, 4, 4 };
        var initial = new bool[3]; // nothing suppressed

        var closed = HierarchySuppression.Close(3, parent, count, initial);

        Assert.False(closed[0] || closed[1] || closed[2]);
        AssertNoSuppressedNodeIsRecoverable(3, parent, count, closed);
    }

    // === Negative self-test: the independent checker is NOT vacuous ===================================

    [Fact]
    public void The_independent_determinability_checker_detects_the_BR1_pre_close_leak()
    {
        // Code-reviewer's guard: prove DeterminedNodes actually FINDS the known pre-close leak, so a future
        // edit that regressed it to a vacuous ∅ would fail here. Over the BR1 repro's INITIAL published set
        // (the per-parent verdict, BEFORE the cross-level Close), P2 (index 2) IS recoverable —
        // AllHig(14) − GA(11) = 3 — and the checker must say so.
        var parent = new[] { -1, 0, 0, 1, 2, 3, 3, 4, 5, 5, 6, 7 };
        var count = new long[] { 14, 11, 3, 11, 3, 7, 4, 3, 4, 3, 4, 3 };
        var initialPublished = new bool[12];
        for (var i = 0; i < 12; i++)
        {
            initialPublished[i] = true;
        }
        foreach (var suppressed in new[] { 1, 2, 4, 7, 9, 8, 11 })
        {
            initialPublished[suppressed] = false;
        }

        var determined = DeterminedNodes(12, parent, count, initialPublished);
        Assert.Contains(2, determined); // P2 recoverable pre-close — the checker is live, not vacuous

        // …and the SAME structure has zero recoverable-suppressed nodes AFTER Close, proving Close closes it.
        var closed = HierarchySuppression.Close(12, parent, count,
            Enumerable.Range(0, 12).Select(i => !initialPublished[i]).ToArray());
        AssertNoSuppressedNodeIsRecoverable(12, parent, count, closed);
    }

    // === Mechanism-independent exact oracle: fuzz the greedy fixpoint against exact linear algebra ======

    [Fact]
    public void The_greedy_fixpoint_holds_against_an_independent_exact_oracle_over_random_trees()
    {
        // Recoverability decided by EXACT rational Gaussian elimination over `parent = Σchildren + phantom`
        // (a wholly different mechanism from the algorithm's single-unknown propagation — and from the
        // laminar-only DeterminedNodes checker). A suppressed node is recoverable iff its unit vector lies in
        // the row space of {published-unit rows} ∪ {equation rows}. Fuzz thousands of random trees: after
        // Close, NO suppressed node may be exactly recoverable (Category=PrivacyReporting — always-on).
        var rng = new Random(20260722);
        for (var iter = 0; iter < 20000; iter++)
        {
            var n = rng.Next(1, 10);
            var parent = new int[n];
            parent[0] = -1;
            for (var i = 1; i < n; i++)
            {
                parent[i] = rng.Next(0, i); // parent index < child index → a valid rooted tree
            }

            // Counts bottom-up so parent ≥ Σchildren always holds (leaves random; internals + random slack).
            var count = new long[n];
            for (var i = n - 1; i >= 0; i--)
            {
                long childSum = 0;
                for (var c = 0; c < n; c++)
                {
                    if (parent[c] == i)
                    {
                        childSum += count[c];
                    }
                }
                count[i] = childSum == 0 ? rng.Next(1, 7) : childSum + rng.Next(0, 4);
            }

            var initial = new bool[n];
            for (var i = 0; i < n; i++)
            {
                initial[i] = count[i] < 4; // the N<4 rule; Close must close everything else itself
            }

            var closed = HierarchySuppression.Close(n, parent, count, initial);

            // Monotonic: nothing initially suppressed becomes published.
            for (var i = 0; i < n; i++)
            {
                if (initial[i])
                {
                    Assert.True(closed[i]);
                }
            }

            var published = new bool[n];
            for (var i = 0; i < n; i++)
            {
                published[i] = !closed[i];
            }
            var recoverable = ExactRecoverableNodes(n, parent, count, published);
            for (var i = 0; i < n; i++)
            {
                if (closed[i])
                {
                    Assert.False(recoverable.Contains(i),
                        $"[seed tree #{iter}] suppressed node {i} (n={count[i]}) is EXACTLY recoverable by differencing");
                }
            }
        }
    }

    /// <summary>Exact oracle: the set of real nodes whose count is uniquely determined by the published nodes
    /// under the linear system {x_p = count_p for published p} ∪ {x_parent = Σx_children + x_phantom}. A node v
    /// is determined iff its unit row e_v lies in the row space of the constraint matrix (over ℚ) — computed by
    /// exact rational row reduction (no floating point), independent of the algorithm's propagation.</summary>
    private static HashSet<int> ExactRecoverableNodes(int n, int[] parent, long[] count, bool[] published)
    {
        var children = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            children[i] = new List<int>();
        }
        for (var i = 0; i < n; i++)
        {
            if (parent[i] >= 0)
            {
                children[parent[i]].Add(i);
            }
        }

        // Columns 0..n-1 are the real nodes; a phantom column is added per internal node with slack > 0.
        var totalCols = n;
        var phantomOf = new int[n];
        for (var i = 0; i < n; i++)
        {
            phantomOf[i] = -1;
            if (children[i].Count > 0)
            {
                long slack = count[i] - children[i].Sum(c => count[c]);
                if (slack > 0)
                {
                    phantomOf[i] = totalCols++;
                }
            }
        }

        var rows = new List<Fraction[]>();
        // Published-unit rows.
        for (var p = 0; p < n; p++)
        {
            if (published[p])
            {
                var row = ZeroRow(totalCols);
                row[p] = Fraction.One;
                rows.Add(row);
            }
        }
        // Equation rows: x_parent − Σ x_children − x_phantom = 0.
        for (var i = 0; i < n; i++)
        {
            if (children[i].Count == 0)
            {
                continue;
            }
            var row = ZeroRow(totalCols);
            row[i] = Fraction.One;
            foreach (var c in children[i])
            {
                row[c] -= Fraction.One;
            }
            if (phantomOf[i] >= 0)
            {
                row[phantomOf[i]] -= Fraction.One;
            }
            rows.Add(row);
        }

        var pivots = RowEchelon(rows, totalCols);
        var recoverable = new HashSet<int>();
        for (var v = 0; v < n; v++)
        {
            var ev = ZeroRow(totalCols);
            ev[v] = Fraction.One;
            if (InRowSpace(pivots, ev, totalCols))
            {
                recoverable.Add(v);
            }
        }
        return recoverable;
    }

    private static Fraction[] ZeroRow(int cols)
    {
        var r = new Fraction[cols];
        for (var i = 0; i < cols; i++)
        {
            r[i] = Fraction.Zero;
        }
        return r;
    }

    /// <summary>Reduce rows to echelon form; returns the pivot rows each normalised to a leading 1, in
    /// increasing pivot-column order.</summary>
    private static List<(int Col, Fraction[] Row)> RowEchelon(List<Fraction[]> rows, int cols)
    {
        var work = rows.Select(r => (Fraction[])r.Clone()).ToList();
        var pivots = new List<(int Col, Fraction[] Row)>();
        var usedRows = new bool[work.Count];
        for (var col = 0; col < cols; col++)
        {
            var pivotRow = -1;
            for (var r = 0; r < work.Count; r++)
            {
                if (!usedRows[r] && !work[r][col].IsZero)
                {
                    pivotRow = r;
                    break;
                }
            }
            if (pivotRow < 0)
            {
                continue;
            }
            usedRows[pivotRow] = true;
            var pr = work[pivotRow];
            var inv = pr[col].Reciprocal();
            for (var j = 0; j < cols; j++)
            {
                pr[j] *= inv; // normalise pivot to 1
            }
            for (var r = 0; r < work.Count; r++)
            {
                if (r != pivotRow && !work[r][col].IsZero)
                {
                    var factor = work[r][col];
                    for (var j = 0; j < cols; j++)
                    {
                        work[r][j] -= factor * pr[j];
                    }
                }
            }
            pivots.Add((col, pr));
        }
        return pivots;
    }

    private static bool InRowSpace(List<(int Col, Fraction[] Row)> pivots, Fraction[] target, int cols)
    {
        var t = (Fraction[])target.Clone();
        foreach (var (col, row) in pivots)
        {
            if (!t[col].IsZero)
            {
                var factor = t[col];
                for (var j = 0; j < cols; j++)
                {
                    t[j] -= factor * row[j];
                }
            }
        }
        for (var j = 0; j < cols; j++)
        {
            if (!t[j].IsZero)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>An exact rational (BigInteger numerator/denominator, always normalised) — no floating point,
    /// so the oracle's recoverability verdict is exact.</summary>
    private readonly struct Fraction
    {
        private readonly BigInteger _num;
        private readonly BigInteger _den; // always > 0

        public static readonly Fraction Zero = new(0, 1);
        public static readonly Fraction One = new(1, 1);

        public Fraction(BigInteger num, BigInteger den)
        {
            if (den.Sign == 0)
            {
                throw new DivideByZeroException();
            }
            if (den.Sign < 0)
            {
                num = -num;
                den = -den;
            }
            var g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
            if (g > BigInteger.One)
            {
                num /= g;
                den /= g;
            }
            _num = num;
            _den = den;
        }

        public bool IsZero => _num.IsZero;

        public Fraction Reciprocal() => new(_den, _num);

        public static Fraction operator +(Fraction a, Fraction b) => new(a._num * b._den + b._num * a._den, a._den * b._den);

        public static Fraction operator -(Fraction a, Fraction b) => new(a._num * b._den - b._num * a._den, a._den * b._den);

        public static Fraction operator *(Fraction a, Fraction b) => new(a._num * b._num, a._den * b._den);
    }
}
