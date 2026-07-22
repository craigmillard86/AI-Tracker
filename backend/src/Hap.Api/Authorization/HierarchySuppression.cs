namespace Hap.Api.Authorization;

/// <summary>
/// Hierarchy-GLOBAL complementary suppression (FR-014 / SC-006, research D2 — the differencing defence
/// extended from a single parent-child level to the whole tree). The per-parent
/// <see cref="SuppressionEvaluator"/> alone leaves a proven leak (HAP-11 red-team BR1): a node suppressed
/// high in the tree can be recovered by summing PUBLISHED nodes lower down — e.g. an all-HIG total minus a
/// published group (its identical single child) recovers a suppressed sub-4 sibling branch's N <b>and</b> its
/// per-dimension mean exactly. Suppressing the parent achieved nothing.
///
/// <para><b>Guarantee.</b> After <see cref="Close"/>, for the reader who can see every published node, no
/// suppressed node's count is <i>determined</i> by the published nodes through the tree's linear identities
/// (<c>parent = Σ children + teamless-slack</c>). Because a node publishes its N and its per-dimension totals
/// together (or neither), the determined-set is identical for counts and for totals, so protecting the count
/// system also protects every mean — closing the mean-recovery half of the attack.</para>
///
/// <para><b>How.</b> (1) <i>Equal-membership collapse</i>: a node with exactly one child and no teamless
/// slack has the SAME people as that child (and transitively down a single-child chain), so if any node on
/// the chain is suppressed they all must be — otherwise the published one reveals the suppressed one directly.
/// (2) <i>Fixpoint</i>: model each parent's identity, treating teamless slack as an always-unknown phantom
/// (it is never published and never itself a protected node). Compute the set of nodes whose count is forced
/// by the currently-published set; if any suppressed node is in it, that is a leak — suppress an additional
/// published node (smallest count first, to keep the useful high-level totals) and repeat until no suppressed
/// node is recoverable. Monotonic (the published set only shrinks), so it terminates.</para>
///
/// <para>Pure and index-based so it is exhaustively unit-testable against hand-built trees (multi-child,
/// deep single-child chains, teamless slack) without a database; <see cref="RollupPipeline"/> maps its node
/// graph onto these arrays and back.</para>
/// </summary>
internal static class HierarchySuppression
{
    /// <summary>
    /// Returns the closed suppression flags (a superset of <paramref name="suppressed"/>) that leave no
    /// suppressed node recoverable by differencing published nodes. <paramref name="parent"/>[i] is the index
    /// of node i's parent, or −1 for a root; <paramref name="count"/>[i] is node i's headcount. Inputs are not
    /// mutated.
    /// </summary>
    public static bool[] Close(int n, int[] parent, long[] count, bool[] suppressed)
    {
        if (n == 0)
        {
            return Array.Empty<bool>();
        }

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

        // Teamless slack per node: how many people sit under the node but in none of its child NODES
        // (a BU's manager-less / cross-BU-managed people; an ungrouped BU under all-HIG). >= 0.
        var slack = new long[n];
        for (var i = 0; i < n; i++)
        {
            var s = count[i];
            foreach (var c in children[i])
            {
                s -= count[c];
            }
            slack[i] = s > 0 ? s : 0;
        }

        // --- (1) equal-membership union-find: merge a node with its only child when there is no slack ------
        var uf = new int[n];
        for (var i = 0; i < n; i++)
        {
            uf[i] = i;
        }
        int Find(int x)
        {
            while (uf[x] != x)
            {
                uf[x] = uf[uf[x]];
                x = uf[x];
            }
            return x;
        }
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
            {
                uf[a] = b;
            }
        }
        for (var i = 0; i < n; i++)
        {
            if (children[i].Count == 1 && slack[i] == 0)
            {
                Union(i, children[i][0]);
            }
        }

        // Class aggregates. A class is suppressed if ANY member is (equal-membership collapse); its count is
        // any member's count (all equal within a class).
        var classSuppressed = new Dictionary<int, bool>();
        var classCount = new Dictionary<int, long>();
        for (var i = 0; i < n; i++)
        {
            var c = Find(i);
            classCount[c] = count[i];
            classSuppressed[c] = classSuppressed.GetValueOrDefault(c) || suppressed[i];
        }

        // --- class-level identities, with a unique phantom variable per non-zero slack -----------------------
        // Variable ids: a class rep is its own index (< n); phantoms take ids >= n. Phantoms are never
        // published and never protected — they only add an unknown so teamless slack cannot leak a node.
        var equations = new List<(int Lhs, List<int> Rhs)>();
        var phantom = n;
        for (var i = 0; i < n; i++)
        {
            if (children[i].Count == 0)
            {
                continue; // a leaf carries no identity of its own
            }
            var lhs = Find(i);
            var rhs = new List<int>();
            foreach (var c in children[i])
            {
                var cc = Find(c);
                if (cc != lhs)
                {
                    rhs.Add(cc); // a merged single child is the same variable — skip the trivial X = X
                }
            }
            if (slack[i] > 0)
            {
                rhs.Add(phantom++);
            }
            if (rhs.Count == 0)
            {
                continue;
            }
            equations.Add((lhs, rhs));
        }

        var published = new HashSet<int>();
        foreach (var c in classCount.Keys)
        {
            if (!classSuppressed[c])
            {
                published.Add(c);
            }
        }

        // Set of variables whose value is forced by `pub` through the identities (transitive closure).
        HashSet<int> Determined(HashSet<int> pub)
        {
            var known = new HashSet<int>(pub);
            bool changed;
            do
            {
                changed = false;
                foreach (var (lhs, rhs) in equations)
                {
                    var unknown = -1;
                    var unknownCount = 0;
                    if (!known.Contains(lhs))
                    {
                        unknown = lhs;
                        unknownCount++;
                    }
                    foreach (var r in rhs)
                    {
                        if (!known.Contains(r))
                        {
                            unknown = r;
                            unknownCount++;
                            if (unknownCount > 1)
                            {
                                break;
                            }
                        }
                    }
                    if (unknownCount == 1 && known.Add(unknown))
                    {
                        changed = true;
                    }
                }
            }
            while (changed);
            return known;
        }

        // Suppressed classes recoverable from `pub` (phantoms, id >= n, are never protected → never a leak).
        List<int> Leaks(HashSet<int> pub)
        {
            var known = Determined(pub);
            var leaks = new List<int>();
            foreach (var c in classCount.Keys)
            {
                if (classSuppressed[c] && !pub.Contains(c) && known.Contains(c))
                {
                    leaks.Add(c);
                }
            }
            return leaks;
        }

        var currentLeaks = Leaks(published).Count;
        var guard = published.Count + 1;
        while (currentLeaks > 0 && guard-- > 0)
        {
            // Suppress the smallest published class whose removal strictly reduces the leak count — this
            // spares the high-value totals (all-HIG, a BU) and hits the small complement nodes instead.
            var ordered = published.OrderBy(x => classCount[x]).ThenBy(x => x).ToList();
            var chosen = -1;
            foreach (var p in ordered)
            {
                var trial = new HashSet<int>(published);
                trial.Remove(p);
                if (Leaks(trial).Count < currentLeaks)
                {
                    chosen = p;
                    break;
                }
            }
            if (chosen < 0)
            {
                chosen = ordered[0]; // fallback: guarantee progress (published strictly shrinks)
            }
            published.Remove(chosen);
            classSuppressed[chosen] = true;
            currentLeaks = Leaks(published).Count;
        }

        var result = (bool[])suppressed.Clone();
        for (var i = 0; i < n; i++)
        {
            if (classSuppressed[Find(i)])
            {
                result[i] = true;
            }
        }
        return result;
    }
}
