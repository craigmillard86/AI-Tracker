using Hap.Api.Authorization;
using Hap.Domain.Org;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// Unit coverage of <see cref="ChainResolver"/> (FR-025 individual-read grant, FR-070 escalation,
/// Q-006 contractor-manager, and the cycle-safe walk). All database-free against hand-built
/// <see cref="OrgGraph"/> fixtures. Category=PrivacyReporting — these are the seam's safeguarding
/// invariants and belong in the always-on regression suite (CLAUDE.md §7).
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class ChainResolverTests
{
    private static ChainResolver Restrictive() => new(SeamOptions.Default);
    private static ChainResolver Permissive() =>
        new(SeamOptions.Default with { ContractorManagerPolicy = ContractorManagerPolicy.Permissive });

    // --- straight chain: individual read follows the chain, and only the chain -------------------

    [Fact]
    public void Self_can_always_read_own_scores()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1").Person("ind", "BU1");
        var g = b.Build();

        Assert.True(Restrictive().GrantsIndividualRead(g, b.Id("ind"), b.Id("ind")));
    }

    [Fact]
    public void Ancestors_up_the_chain_can_read_a_subject_a_stranger_cannot()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("evp", "BU1")
            .Person("dir", "BU1", manager: "evp")
            .Person("lead", "BU1", manager: "dir")
            .Person("ind", "BU1", manager: "lead")
            .Person("stranger", "BU1", manager: "evp");
        var g = b.Build();
        var chain = Restrictive();

        Assert.True(chain.GrantsIndividualRead(g, b.Id("lead"), b.Id("ind")));   // direct manager
        Assert.True(chain.GrantsIndividualRead(g, b.Id("dir"), b.Id("ind")));    // skip-level
        Assert.True(chain.GrantsIndividualRead(g, b.Id("evp"), b.Id("ind")));    // top of BU
        Assert.False(chain.GrantsIndividualRead(g, b.Id("stranger"), b.Id("ind"))); // off the chain
        Assert.False(chain.GrantsIndividualRead(g, b.Id("ind"), b.Id("stranger"))); // a report cannot read up/side
    }

    // --- manager gap (FR-069): null manager ends the chain --------------------------------------

    [Fact]
    public void Manager_gap_ends_the_chain_only_self_reads()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1").Person("orphan", "BU1", manager: null);
        var g = b.Build();
        var chain = Restrictive();

        Assert.Empty(chain.UpwardChain(g, b.Id("orphan")));
        Assert.True(chain.GrantsIndividualRead(g, b.Id("orphan"), b.Id("orphan")));
    }

    [Fact]
    public void Dangling_manager_reference_ends_the_chain_and_grants_nobody()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1").PersonWithDanglingManager("ind", "BU1");
        var g = b.Build();
        var chain = Restrictive();

        Assert.Empty(chain.UpwardChain(g, b.Id("ind")));
    }

    // --- on-leave (FR-069): on-leave leaves IsActive true, so the manager stays in the chain -----

    [Fact]
    public void On_leave_manager_remains_in_the_chain()
    {
        // On-leave (FR-069) does NOT deactivate — IsActive stays true — so an on-leave manager is just
        // an ordinary active ancestor to the seam. Model that directly: an active manager grants read.
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("mgr_on_leave", "BU1", active: true)
            .Person("ind", "BU1", manager: "mgr_on_leave");
        var g = b.Build();
        var chain = Restrictive();

        Assert.Contains(b.Id("mgr_on_leave"), chain.UpwardChain(g, b.Id("ind")));
        Assert.True(chain.GrantsIndividualRead(g, b.Id("mgr_on_leave"), b.Id("ind")));
    }

    // --- departed manager (FR-070): edge still traversed, but the departed manager loses access --

    [Fact]
    public void Departed_inactive_manager_loses_access_but_chain_reaches_their_manager()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("evp", "BU1")
            .Person("departed", "BU1", manager: "evp", active: false)
            .Person("ind", "BU1", manager: "departed");
        var g = b.Build();
        var chain = Restrictive();

        Assert.False(chain.GrantsIndividualRead(g, b.Id("departed"), b.Id("ind"))); // inactive → denied
        Assert.True(chain.GrantsIndividualRead(g, b.Id("evp"), b.Id("ind")));       // reachable through them
        Assert.Equal(b.Id("evp"), chain.EscalationManager(g, b.Id("ind")));
        Assert.Equal(b.Id("evp"), chain.ReviewerOfRecord(g, b.Id("ind")));          // departed manager skipped
    }

    // --- cross-BU chain: the chain governs regardless of BU membership --------------------------

    [Fact]
    public void Cross_BU_manager_reads_a_report_homed_in_a_different_BU()
    {
        var b = new GraphBuilder()
            .Bu("BU1", "G1", "P1").Bu("BU2", "G1", "P1")
            .Person("evp1", "BU1")
            .Person("xbu", "BU2", manager: "evp1"); // homed in BU2, managed from BU1
        var g = b.Build();
        var chain = Restrictive();

        Assert.True(chain.GrantsIndividualRead(g, b.Id("evp1"), b.Id("xbu")));
    }

    // --- contractor manager (Q-006): RESTRICTIVE default excludes them, escalates past them ------

    [Fact]
    public void Contractor_manager_is_denied_and_reviews_escalate_under_restrictive_default()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("evp", "BU1")
            .Person("ctr", "BU1", manager: "evp", type: EmployeeType.Contractor)
            .Person("r1", "BU1", manager: "ctr");
        var g = b.Build();
        var chain = Restrictive();

        Assert.False(chain.GrantsIndividualRead(g, b.Id("ctr"), b.Id("r1")));  // contractor excluded
        Assert.True(chain.GrantsIndividualRead(g, b.Id("evp"), b.Id("r1")));   // employee above keeps access
        Assert.Equal(b.Id("evp"), chain.ReviewerOfRecord(g, b.Id("r1")));      // escalates past the contractor
        Assert.Equal(b.Id("evp"), chain.EscalationManager(g, b.Id("r1")));
    }

    [Fact]
    public void Contractor_manager_reads_when_policy_is_permissive()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("evp", "BU1")
            .Person("ctr", "BU1", manager: "evp", type: EmployeeType.Contractor)
            .Person("r1", "BU1", manager: "ctr");
        var g = b.Build();

        Assert.True(Permissive().GrantsIndividualRead(g, b.Id("ctr"), b.Id("r1")));
        Assert.Equal(b.Id("ctr"), Permissive().ReviewerOfRecord(g, b.Id("r1")));
    }

    // --- cycle-safe walk (HAP-3 red-team carry-forward): 2-cycle terminates, grants nothing wrong-

    [Fact]
    public void Two_node_management_cycle_terminates_and_grants_no_access_it_should_not()
    {
        // A.mgr = B, B.mgr = A — importable via a future non-synthetic adapter. The read-side backstop
        // must terminate and must not grant a stranger access to either member.
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("A", "BU1", manager: "B")
            .Person("B", "BU1", manager: "A")
            .Person("stranger", "BU1");
        var g = b.Build();
        var chain = Restrictive();

        // Terminates (no hang / stack overflow) and yields a finite, non-repeating chain.
        var chainOfA = chain.UpwardChain(g, b.Id("A"));
        var chainOfB = chain.UpwardChain(g, b.Id("B"));
        Assert.Equal(new[] { b.Id("B") }, chainOfA);
        Assert.Equal(new[] { b.Id("A") }, chainOfB);

        // Each cycle member can read the other (they are genuinely each other's recorded manager),
        // but nobody outside the cycle gains access, and the walk never loops.
        Assert.True(chain.GrantsIndividualRead(g, b.Id("B"), b.Id("A")));
        Assert.False(chain.GrantsIndividualRead(g, b.Id("stranger"), b.Id("A")));
        Assert.False(chain.GrantsIndividualRead(g, b.Id("stranger"), b.Id("B")));
    }

    [Fact]
    public void Self_referential_manager_does_not_loop()
    {
        var b = new GraphBuilder().Bu("BU1", "G1", "P1").Person("solo", "BU1", manager: "solo");
        var g = b.Build();
        var chain = Restrictive();

        Assert.Empty(chain.UpwardChain(g, b.Id("solo"))); // visited-set contains the subject already
        Assert.True(chain.GrantsIndividualRead(g, b.Id("solo"), b.Id("solo")));
    }

    // === QA ADVERSARIAL (hap-qa) — cycle-safety generalisation ======================================
    // The AC's proof requirement is a hostile 2-cycle fixture; these extend the same attack shape to a
    // longer cycle and to a non-cyclic pathological depth, to confirm the visited-set + depth-cap guards
    // generalise rather than being tuned to the 2-node case specifically.

    [Fact]
    public void Three_node_management_cycle_terminates_and_grants_no_access_it_should_not()
    {
        // A.mgr = B, B.mgr = C, C.mgr = A — a longer hostile cycle than the AC's proof fixture.
        var b = new GraphBuilder().Bu("BU1", "G1", "P1")
            .Person("A", "BU1", manager: "C")
            .Person("B", "BU1", manager: "A")
            .Person("C", "BU1", manager: "B")
            .Person("stranger", "BU1");
        var g = b.Build();
        var chain = Restrictive();

        var chainOfA = chain.UpwardChain(g, b.Id("A"));
        Assert.Equal(new[] { b.Id("C"), b.Id("B") }, chainOfA); // terminates, no repeat, no hang

        Assert.True(chain.GrantsIndividualRead(g, b.Id("C"), b.Id("A")));  // direct manager on the cycle
        Assert.True(chain.GrantsIndividualRead(g, b.Id("B"), b.Id("A")));  // skip-level, still on the cycle
        Assert.False(chain.GrantsIndividualRead(g, b.Id("stranger"), b.Id("A"))); // off the cycle entirely
    }

    [Fact]
    public void Depth_cap_terminates_a_long_acyclic_chain_without_hanging()
    {
        // ATTACK: a straight (non-cyclic) chain deeper than MaxChainDepth (64) — the visited-set alone
        // would happily walk this to completion, so this proves the depth cap is an independent, live
        // backstop, not dead code shadowed by the cycle guard.
        var b = new GraphBuilder().Bu("BU1", "G1", "P1");
        const int depth = 100; // p0 (subject, most junior) -> p1 -> p2 -> ... -> p99 (root, no manager)
        for (int i = 0; i < depth; i++)
        {
            string? managerAlias = i == depth - 1 ? null : $"p{i + 1}";
            b.Person($"p{i}", "BU1", manager: managerAlias);
        }
        var g = b.Build();
        var chain = Restrictive();

        var upward = chain.UpwardChain(g, b.Id("p0"));
        Assert.Equal(SeamOptions.Default.MaxChainDepth, upward.Count); // capped, not the full 99 ancestors

        // The top-most ancestor (p99, the root) is beyond the cap and must NOT be granted access to p0.
        Assert.False(chain.GrantsIndividualRead(g, b.Id($"p{depth - 1}"), b.Id("p0")));
    }
}
