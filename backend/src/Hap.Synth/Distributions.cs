namespace Hap.Synth;

// ============================================================================
//  PROTECTED SYNTHETIC DISTRIBUTIONS — CHANGES ARE REVIEWED, NEVER SILENT
// ----------------------------------------------------------------------------
//  Constitution Art. X / CLAUDE.md §13: "Don't silently change a synth
//  generator's distributions." Every population shape, rate, and engineered
//  edge case for the synthetic directory lives HERE, in one file, so that any
//  change to the statistical character of the generated org is a visible,
//  reviewable diff to this table — not a value buried in generation logic.
//
//  Editing any constant below changes the synthetic population and MUST go
//  through the L2 review panel (hap-code-reviewer + hap-domain-specialist).
//  The generator (DirectoryGenerator.cs) reads from here and contains no
//  magic numbers of its own.
//
//  Invariants these values are tuned to satisfy (asserted by Hap.Synth.Tests):
//    * exactly 23 BUs across 6 groups / 3 portfolios
//    * total >= 10,000 people; >= 2,000 distinct managers with >= 1 active report
//    * ordinary (non-engineered) BUs each hold 300-800 people
//    * >= 5% contractors, spread across BUs
//  See QUESTIONS.md Q-009 for the 300-800-vs-engineered-small-BU reconciliation.
// ============================================================================

/// <summary>
/// The one reviewed source of every distribution parameter and engineered edge
/// case in the synthetic directory. See the file header for the review contract.
/// </summary>
public static class Distributions
{
    /// <summary>Bumped whenever the generation algorithm or this table changes.</summary>
    public const string GeneratorVersion = "1.0.0";

    /// <summary>Canonical seed used by <c>scripts/synth/generate.sh</c> and recorded in output.</summary>
    public const long CanonicalSeed = 20260721L;

    // --- Fixed org skeleton (SC-008) -----------------------------------------
    public const int PortfolioCount = 3;
    public const int GroupCount = 6;                 // 2 groups per portfolio
    public const int BusinessUnitCount = 23;

    /// <summary>BU count per group, summing to <see cref="BusinessUnitCount"/> (4+4+4+4+4+3).</summary>
    public static readonly IReadOnlyList<int> BusPerGroup = new[] { 4, 4, 4, 4, 4, 3 };

    // --- Ordinary BU internal shape ------------------------------------------
    //  A BU is a tree: BU Lead -> Directors -> Team Leads -> Members. Tuned so
    //  ~20% of an ordinary BU are managers, giving >= 2,000 managers overall,
    //  and each ordinary BU lands well inside 300-800 (max left below 800 to
    //  leave headroom for org-top leaders homed in a BU and engineered injections).
    public const int OrdinaryBuMinPeople = 450;
    public const int OrdinaryBuMaxPeople = 680;
    public const int ReportsPerTeamMin = 3;          // members under a team lead
    public const int ReportsPerTeamMax = 6;
    public const int TeamsPerDirectorMin = 6;        // team leads under a director
    public const int TeamsPerDirectorMax = 10;

    /// <summary>Below this leftover headcount, attach people directly under the BU Lead
    /// rather than spinning up a director/team-lead with too few reports.</summary>
    public const int MinPeopleForDirector = 3;

    // --- Member attribute rates (applied to leaf members only) ---------------
    public const double ContractorRate = 0.09;       // >= 5% required; 9% for margin
    public const double LeaverRate = 0.01;           // is_active = false
    public const double OnLeaveRate = 0.02;          // on_leave = true (still active)

    // --- Engineered small BUs (Q-009): deliberate exceptions to 300-800 ------
    //  Chosen so no group is left degenerate and no upper leader is homed in one.
    public const string SubFourBuCode = "BU20";          // edge (b): < 4 people total
    public const string SingleTeamBuCode = "BU12";       // edge (c): exactly one team
    public const string OrgOfSevenBuCode = "BU04";       // edge (d): team of 4 in org of 7

    public const int SingleTeamBuMembers = 7;            // + BU Lead = 8, one team
    public const int SubFourBuMembers = 2;               // + BU Lead = 3 (< 4)

    // --- Engineered fixtures land in these ordinary BUs ----------------------
    public const string PrimaryFixtureBuCode = "BU01";   // seed users + most edge cases
    public const string CrossBuReportBuCode = "BU02";    // edge (i): report homed here,
                                                         // managed from BU01

    // --- Stable external refs for engineered identities ----------------------
    //  These never shift across runs regardless of distribution tuning, so
    //  downstream stories (seed users, seam edge-case tests) can hard-reference them.
    public const string ExecRef = "HAP-EXEC";
    public const string AdminRef = "HAP-ADMIN";
    public const string SeedManagerRef = "HAP-SEED-MGR";
    public const string SeedIndividualRef = "HAP-SEED-IND";
    public const string Team3ManagerRef = "HAP-EDGE-TEAM3-MGR";   // edge (a): team of 3 people
    public const string Team4ManagerRef = "HAP-EDGE-TEAM4-MGR";   // edge (d): team of 4 people
    public const string NullManagerRef = "HAP-EDGE-NULLMGR";      // edge (e): manager gap
    public const string ContractorManagerRef = "HAP-EDGE-CTR-MGR"; // edge (j): contractor manages employees
    public const string LeaverRef = "HAP-EDGE-LEAVER";           // edge (g)
    public const string OnLeaveRef = "HAP-EDGE-ONLEAVE";         // edge (h)
    public const string CrossBuReportRef = "HAP-EDGE-XBU-REPORT"; // edge (i)

    public static string PortfolioLeaderRef(int index1) => $"HAP-PF-{index1:D2}";
    public static string GroupLeaderRef(int index1) => $"HAP-GRP-{index1:D2}";
    public static string BuLeadRef(int index1) => $"HAP-BUL-{index1:D2}";
    public static string BuCode(int index1) => $"BU{index1:D2}";
}
