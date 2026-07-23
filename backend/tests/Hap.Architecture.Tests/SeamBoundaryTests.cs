using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// The seam-isolation guard (research D1, HAP-5 AC; extended by HAP-8 with the migration). The
/// assessment entity types (<c>Assessment</c>/<c>AssessmentScore</c>), their table names, and — new in
/// HAP-8 — their <c>DbSet</c>/<c>Set&lt;&gt;()</c> QUERY SURFACE may be referenced ONLY inside a small,
/// enumerated set of sanctioned locations. Any other production file naming them is a query path that
/// could bypass the authorisation layer, so the build must fail.
///
/// <para><b>Sanctioned locations (allowlist).</b> Each is a definition/schema/query-inside-the-seam
/// location, never an ad-hoc read path:
/// <list type="bullet">
/// <item><c>backend/src/Hap.Api/Authorization/</c> — the visibility seam (the gateway + the ONE store
/// that holds the <c>Set&lt;&gt;()</c> query surface).</item>
/// <item><c>backend/src/Hap.Domain/Assessments/</c> — where the entity TYPES are DEFINED (HAP-8
/// relocation, HAP-5 handoff). Defining a type is not a query path.</item>
/// <item><c>Hap.Infrastructure/Persistence/AssessmentEntityConfiguration.cs</c> — the single EF
/// schema-mapping file (<c>IEntityTypeConfiguration</c>), applied by <c>HapDbContext</c> so the context
/// itself never spells the bare tokens. Mapping is schema, not a query.</item>
/// <item><c>Hap.Infrastructure/Persistence/Migrations/</c> — generated DDL + model snapshot.</item>
/// </list></para>
///
/// <para>Matching is CASE-SENSITIVE on the PascalCase type identifiers (prose across the codebase uses
/// lowercase "assessment" freely — that is documentation, not a query path) plus the underscored table
/// name (case-insensitive; it never occurs in prose) plus the DbSet/Set query-surface forms.
/// Category=PrivacyReporting — it runs in the always-on regression suite.</para>
/// </summary>
public class SeamBoundaryTests
{
    // Paths permitted to name the assessment types / table names / query surface. A file is allowed if
    // its normalised path CONTAINS any one of these markers.
    private static readonly string[] AllowedMarkers =
    {
        "Hap.Api/Authorization/",                                    // the visibility seam
        "Hap.Domain/Assessments/",                                   // the entity type definitions
        "Hap.Infrastructure/Persistence/AssessmentEntityConfiguration.cs", // the single EF-mapping file
        "Hap.Infrastructure/Persistence/Migrations/",                // generated schema DDL + snapshot
    };

    private static readonly Regex[] AssessmentReferencePatterns =
    {
        new(@"\bAssessments?\b", RegexOptions.Compiled),                          // Assessment / Assessments (type)
        new(@"\bAssessmentScores?\b", RegexOptions.Compiled),                     // AssessmentScore(s) (type)
        new(@"\bassessment_scores?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), // table name
        // The DbSet/Set query surface (HAP-8): the only forms that actually READ the tables. Explicit so
        // the guard's intent — "no query path outside the seam" — is legible even though the bare-type
        // patterns above already subsume them. `Set<Assessment>` also covers `Set<AssessmentScore>`.
        new(@"DbSet<\s*Assessment", RegexOptions.Compiled),
        new(@"\bSet<\s*Assessment", RegexOptions.Compiled),
    };

    // --- HAP-11 BB3: the RollupSnapshot QUERY SURFACE (public DbSet) is structurally seam-bound ---------
    // The snapshot TYPE is a public domain type freely named in DTOs/projections (unlike the assessment
    // types), so the guard targets only the QUERY SURFACE — the `RollupSnapshots` DbSet access and
    // `Set<RollupSnapshot>()` — which is the only way to read raw snapshot figures. Restricting it to the
    // seam makes F2 STRUCTURAL: a future non-seam file doing `db.RollupSnapshots.Where(...).Select(raw N/mean)`
    // for a suppressed node fails the build, rather than merely being "today's-code clean".
    private static readonly string[] RollupQueryAllowedMarkers =
    {
        "Hap.Api/Authorization/",                                 // the seam: RollupReads + CycleCloseProcessor
        "Hap.Infrastructure/HapDbContext.cs",                     // the DbSet DEFINITION
        "Hap.Infrastructure/Persistence/",                        // EF mapping + generated migrations/snapshot
        "Hap.Domain/Rollups/",                                    // the entity type definition
    };

    private static readonly Regex[] RollupQuerySurfacePatterns =
    {
        new(@"\.RollupSnapshots\b", RegexOptions.Compiled),                      // the DbSet property access
        new(@"Set<\s*(?:[A-Za-z_][\w.]*\.)?RollupSnapshot\b", RegexOptions.Compiled), // Set<[...]RollupSnapshot>
    };

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void RollupSnapshot_query_surface_is_referenced_only_inside_the_seam()
    {
        var files = RepoSource.CsFiles().Select(path => (Path: path, Lines: File.ReadAllLines(path)));

        var offenders = FindReferencesOutsideAllowlist(files, RollupQueryAllowedMarkers, RollupQuerySurfacePatterns);

        Assert.True(offenders.Count == 0,
            "The RollupSnapshots DbSet / Set<RollupSnapshot> query surface must be read only inside the " +
            "visibility seam (Hap.Api/Authorization), where every read is projected through the F2 " +
            "suppression guard. Any other query path could emit raw N/mean/distribution for a suppressed " +
            "node (FR-071). Offending lines:\n" + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void RollupSnapshot_query_guard_is_not_vacuous_and_flags_a_leak_outside_the_seam()
    {
        // Canary: the seam genuinely queries the DbSet, so scanning with a match-nothing allowlist finds it.
        var files = RepoSource.CsFiles().Select(path => (Path: path, Lines: File.ReadAllLines(path)));
        var matches = FindReferencesOutsideAllowlist(files, new[] { "____no_such_dir____" }, RollupQuerySurfacePatterns);
        Assert.True(matches.Count > 0,
            "Expected the RollupSnapshots query surface to be used SOMEWHERE in backend/src (the seam reads it) " +
            "— zero matches means the guard is vacuous.");

        // Negative case: a non-seam service querying the snapshots directly for raw figures MUST be caught.
        var leak = ("backend/src/Hap.Api/RawSnapshotLeak.cs",
            new[] { "var raw = db.RollupSnapshots.Where(s => s.Suppressed).Select(s => s.N).ToList();" });
        Assert.NotEmpty(FindReferencesOutsideAllowlist(new[] { leak }, RollupQueryAllowedMarkers, RollupQuerySurfacePatterns));

        // The identical query INSIDE the seam is allowed (it is projected through the F2 guard there).
        var inSeam = ("backend/src/Hap.Api/Authorization/RollupReads.cs",
            new[] { "var raw = db.RollupSnapshots.Where(s => s.Suppressed).Select(s => s.N).ToList();" });
        Assert.Empty(FindReferencesOutsideAllowlist(new[] { inSeam }, RollupQueryAllowedMarkers, RollupQuerySurfacePatterns));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Assessment_types_and_query_surface_are_referenced_only_in_sanctioned_locations()
    {
        var files = RepoSource.CsFiles()
            .Select(path => (Path: path, Lines: File.ReadAllLines(path)));

        var offenders = FindReferencesOutsideAllowlist(files, AllowedMarkers, AssessmentReferencePatterns);

        Assert.True(offenders.Count == 0,
            "Assessment entity types/table names/DbSet query surface must be referenced only inside the " +
            "sanctioned locations (the visibility seam, the domain type definitions, the single EF-mapping " +
            "file, and generated migrations) — every other reference is a potential read path bypassing the " +
            "authorisation layer (research D1). Offending lines:\n" + string.Join("\n", offenders));
    }

    // --- canary: the real enumeration path actually yields matchable content -------------------

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Real_source_scan_is_not_vacuous_the_sanctioned_locations_contain_the_tokens()
    {
        // Scan the real files with an allowlist that matches NOTHING, so the sanctioned locations' own
        // Assessment references count as "outside". If this finds nothing, the guard above is passing
        // vacuously (empty corpus, broken enumeration, or patterns that match nothing) — which would hide
        // a real leak. The seam + domain + mapping define/query these types, so this MUST find matches.
        var files = RepoSource.CsFiles()
            .Select(path => (Path: path, Lines: File.ReadAllLines(path)));

        var matches = FindReferencesOutsideAllowlist(
            files, new[] { "____no_such_dir____" }, AssessmentReferencePatterns);

        Assert.True(matches.Count > 0,
            "Expected the Assessment types to be referenced SOMEWHERE in backend/src (the seam, the domain " +
            "definitions, and the EF mapping name them) — zero matches means the scan is vacuous and the " +
            "boundary guard proves nothing.");
    }

    // --- the guard's own negative-case proof: it is not vacuous --------------------------------

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Guard_flags_a_reference_outside_the_sanctioned_locations_but_not_inside_them()
    {
        // A synthetic "leak": a domain-layer file OUTSIDE the Assessments definition folder naming the
        // type. The detector MUST catch it — proving the guard would fail the build if such a reference
        // were ever introduced (e.g. a service querying the tables directly).
        var leak = ("backend/src/Hap.Domain/Leak.cs",
            new[] { "public sealed class Leak { private Assessment _a = null!; }" });

        var caught = FindReferencesOutsideAllowlist(new[] { leak }, AllowedMarkers, AssessmentReferencePatterns);
        Assert.Single(caught);

        // The identical content INSIDE the seam is allowed — proving the allowlist actually gates on path.
        var inSeam = ("backend/src/Hap.Api/Authorization/Leak.cs",
            new[] { "public sealed class Leak { private Assessment _a = null!; }" });
        Assert.Empty(FindReferencesOutsideAllowlist(new[] { inSeam }, AllowedMarkers, AssessmentReferencePatterns));

        // …as is naming the type inside its definition folder (a definition is not a query path).
        var inDefinition = ("backend/src/Hap.Domain/Assessments/Assessment.cs",
            new[] { "public sealed class Assessment { }" });
        Assert.Empty(FindReferencesOutsideAllowlist(new[] { inDefinition }, AllowedMarkers, AssessmentReferencePatterns));

        // A raw DbSet query surface OUTSIDE the seam is caught by the DbSet-form patterns specifically.
        var dbSetLeak = ("backend/src/Hap.Infrastructure/HapDbContext.cs",
            new[] { "public DbSet<Assessment> Assessments => Set<Assessment>();" });
        Assert.NotEmpty(FindReferencesOutsideAllowlist(new[] { dbSetLeak }, AllowedMarkers, AssessmentReferencePatterns));
    }

    // --- HAP-12: raw-score DISPLAY reads must consult the erasure ledger --------------------------------
    // The defect class QA found: a view-builder reads AssessmentScore rows and serves them without checking
    // the RetentionErasure ledger, so a retention-erased assessment's fabricated placeholder (self→0) is
    // presented as genuine (the B1 leak, reopened on GetMemberAssessmentAsync). Convention ("remember to
    // check the ledger") is what let it reopen — this makes it STRUCTURAL: any production file that reads
    // assessment scores for display (one of the store's assessment-with-scores read methods) MUST also
    // reference the shared ErasureLedger, or the build fails. Exempt: the raw store impl + the interface that
    // DEFINE these methods, and the low-level chain gateway (AssessmentReads) that returns raw scores to the
    // seam by design — erasure disclosure/refusal happens at the view-builder that consumes them.
    private static readonly string[] DisplayReadDefiningFiles =
    {
        "Hap.Api/Authorization/AssessmentData.cs",        // the interface declaring the methods
        "Hap.Api/Authorization/SeamAssessmentStore.cs",   // the raw store implementing them
        "Hap.Api/Authorization/AssessmentReads.cs",       // the chain gateway (returns raw scores to the seam)
    };

    private static readonly Regex DisplayReadMethods = new(
        @"\b(GetAssessmentWithScoresAsync|GetByIdWithScoresAsync|GetSelfAsync|GetSelfScoresForCycleAsync|GetIndividualScoresAsync|ReadIndividualScoresAsync|GetAllForPersonAsync)\b",
        RegexOptions.Compiled);

    private static readonly Regex ErasureLedgerReference = new(
        @"\b(ErasureLedger|IsErasedAsync|ErasedAssessmentIdsAsync|AllErasedAssessmentIdsAsync)\b",
        RegexOptions.Compiled);

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Raw_score_display_reads_consult_the_erasure_ledger()
    {
        var files = RepoSource.CsFiles().Select(path => (Path: path, Lines: File.ReadAllLines(path)));
        var offenders = FindDisplayReadsWithoutErasureCheck(files, DisplayReadDefiningFiles);

        Assert.True(offenders.Count == 0,
            "Every raw-score DISPLAY read must consult the shared ErasureLedger so a retention-erased " +
            "assessment is disclosed/refused, never served as a fabricated 0 (FR-052, HAP-12 QA finding). " +
            "These files read assessment scores but never reference the ledger:\n" + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Erasure_ledger_display_guard_is_not_vacuous_and_flags_an_unguarded_read()
    {
        // Canary: real view-builders DO read scores, so scanning with a match-nothing exemption finds them.
        var files = RepoSource.CsFiles().Select(path => (Path: path, Lines: File.ReadAllLines(path)));
        Assert.NotEmpty(FindDisplayReadsWithoutErasureCheck(files, new[] { "____no_such_file____" }));

        // Negative case: a NEW view-builder reading scores WITHOUT the ledger MUST be caught.
        var leak = ("backend/src/Hap.Api/Authorization/NewView.cs",
            new[] { "var current = await _store.GetSelfAsync(personId, cycleId, ct);", "return Project(current);" });
        Assert.NotEmpty(FindDisplayReadsWithoutErasureCheck(new[] { leak }, DisplayReadDefiningFiles));

        // The same read WITH a ledger reference in the file is allowed (it discloses/refuses erasure).
        var guarded = ("backend/src/Hap.Api/Authorization/GuardedView.cs",
            new[] { "var current = await _store.GetSelfAsync(personId, cycleId, ct);",
                    "if (await _ledger.IsErasedAsync(personId, current.Assessment.Id, ct)) return Disclosed();" });
        Assert.Empty(FindDisplayReadsWithoutErasureCheck(new[] { guarded }, DisplayReadDefiningFiles));
    }

    /// <summary>Detector: a file is an offender if it references a display-read method, is NOT one of the
    /// <paramref name="exemptFiles"/> (raw store / interface / gateway), and NEVER references the erasure
    /// ledger. Whole-file check (the ledger reference may be lines away from the read).</summary>
    internal static IReadOnlyList<string> FindDisplayReadsWithoutErasureCheck(
        IEnumerable<(string Path, string[] Lines)> files, IReadOnlyList<string> exemptFiles)
    {
        var offenders = new List<string>();
        foreach (var (path, lines) in files)
        {
            var normalised = path.Replace('\\', '/');
            if (exemptFiles.Any(marker => normalised.Contains(marker, StringComparison.Ordinal)))
            {
                continue;
            }
            var readsScores = false;
            var checksLedger = false;
            foreach (var line in lines)
            {
                if (DisplayReadMethods.IsMatch(line)) readsScores = true;
                if (ErasureLedgerReference.IsMatch(line)) checksLedger = true;
            }
            if (readsScores && !checksLedger)
            {
                offenders.Add($"{path}: reads assessment scores for display but never references the ErasureLedger");
            }
        }
        return offenders;
    }

    /// <summary>Pure detector: returns "path:line: 'pattern'" for every match in a file whose normalised
    /// path contains NONE of <paramref name="allowedMarkers"/>. Path separators are normalised so the
    /// check is OS-agnostic.</summary>
    internal static IReadOnlyList<string> FindReferencesOutsideAllowlist(
        IEnumerable<(string Path, string[] Lines)> files,
        IReadOnlyList<string> allowedMarkers,
        IReadOnlyList<Regex> patterns)
    {
        var offenders = new List<string>();
        foreach (var (path, lines) in files)
        {
            var normalised = path.Replace('\\', '/');
            if (allowedMarkers.Any(marker => normalised.Contains(marker, StringComparison.Ordinal)))
            {
                continue; // inside a sanctioned location — allowed
            }
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var pattern in patterns)
                {
                    if (pattern.IsMatch(lines[i]))
                    {
                        offenders.Add($"{path}:{i + 1}: matches /{pattern}/");
                    }
                }
            }
        }
        return offenders;
    }
}
