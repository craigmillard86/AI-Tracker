using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// The seam-isolation guard (research D1, HAP-5 AC): the assessment entity types
/// (<c>Assessment</c>/<c>AssessmentScore</c>) and their table names may be referenced ONLY inside
/// <c>backend/src/Hap.Api/Authorization/**</c> — the visibility seam. Any other production namespace
/// naming them is a query path that could bypass the authorisation layer, so the build must fail.
///
/// <para>Matching is CASE-SENSITIVE on the PascalCase type identifiers (prose across the codebase uses
/// lowercase "assessment" freely — that is documentation, not a query path) plus the underscored table
/// name (which never occurs in prose). The DbSet form of this guard, and the full table-name coverage,
/// are added by HAP-8 with the migration; today no DbSet or migration exists, so this guards the TYPE
/// references that DO exist. Category=PrivacyReporting — it runs in the always-on regression suite.</para>
/// </summary>
public class SeamBoundaryTests
{
    // The one namespace/folder permitted to name the assessment types.
    private const string SeamMarker = "Hap.Api/Authorization/";

    private static readonly Regex[] AssessmentReferencePatterns =
    {
        new(@"\bAssessments?\b", RegexOptions.Compiled),                          // Assessment / Assessments (type)
        new(@"\bAssessmentScores?\b", RegexOptions.Compiled),                     // AssessmentScore(s) (type)
        new(@"\bassessment_scores?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), // table name (forward-looking)
    };

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Assessment_types_are_referenced_only_inside_the_visibility_seam()
    {
        var files = RepoSource.CsFiles()
            .Select(path => (Path: path, Lines: File.ReadAllLines(path)));

        var offenders = FindReferencesOutsideSeam(files, SeamMarker, AssessmentReferencePatterns);

        Assert.True(offenders.Count == 0,
            "Assessment entity types/table names must be referenced only inside " + SeamMarker +
            " (the visibility seam) — every other reference is a potential read path bypassing the " +
            "authorisation layer (research D1). Offending lines:\n" + string.Join("\n", offenders));
    }

    // --- canary: the real enumeration path actually yields matchable content -------------------

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Real_source_scan_is_not_vacuous_the_seam_itself_contains_the_tokens()
    {
        // Scan the real files with a seam marker that matches NOTHING, so the seam's own Assessment
        // references count as "outside". If this finds nothing, the guard above is passing vacuously
        // (empty corpus, broken enumeration, or patterns that match nothing) — which would hide a real
        // leak. The seam defines Assessment/AssessmentScore, so this MUST find matches.
        var files = RepoSource.CsFiles()
            .Select(path => (Path: path, Lines: File.ReadAllLines(path)));

        var matches = FindReferencesOutsideSeam(files, "____no_such_seam_dir____", AssessmentReferencePatterns);

        Assert.True(matches.Count > 0,
            "Expected the Assessment types to be referenced SOMEWHERE in backend/src (the seam defines " +
            "them) — zero matches means the scan is vacuous and the boundary guard proves nothing.");
    }

    // --- the guard's own negative-case proof: it is not vacuous --------------------------------

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Guard_flags_a_reference_outside_the_seam_but_not_inside_it()
    {
        // A synthetic "leak": a domain-layer file naming the type. The detector MUST catch it — proving
        // the guard above would fail the build if such a reference were ever introduced.
        var leak = ("backend/src/Hap.Domain/Leak.cs",
            new[] { "public sealed class Leak { private Assessment _a = null!; }" });

        var caught = FindReferencesOutsideSeam(new[] { leak }, SeamMarker, AssessmentReferencePatterns);
        Assert.Single(caught);

        // The identical content INSIDE the seam is allowed — proving the allowlist actually gates on path.
        var inSeam = ("backend/src/Hap.Api/Authorization/Leak.cs",
            new[] { "public sealed class Leak { private Assessment _a = null!; }" });
        var allowed = FindReferencesOutsideSeam(new[] { inSeam }, SeamMarker, AssessmentReferencePatterns);
        Assert.Empty(allowed);
    }

    /// <summary>Pure detector: returns "path:line: 'pattern'" for every match in a file whose path does
    /// not contain <paramref name="seamMarker"/>. Path separators are normalised so the check is
    /// OS-agnostic.</summary>
    internal static IReadOnlyList<string> FindReferencesOutsideSeam(
        IEnumerable<(string Path, string[] Lines)> files, string seamMarker, IReadOnlyList<Regex> patterns)
    {
        var offenders = new List<string>();
        foreach (var (path, lines) in files)
        {
            var normalised = path.Replace('\\', '/');
            if (normalised.Contains(seamMarker, StringComparison.Ordinal))
            {
                continue; // inside the seam — allowed
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
