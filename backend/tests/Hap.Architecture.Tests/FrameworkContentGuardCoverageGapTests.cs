using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hap.Infrastructure.Frameworks;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-6 (fresh-instance QA pass, CLAUDE.md §9; constitution
/// Art. II.4) — probes the exact coverage gap the L2 panel flagged and deliberately left
/// unfixed (round-1 advisory A1/A3 fix note on <see cref="FrameworkContentNotHardcodedTests"/>):
/// the differentiated length floor (<c>MinSingleWordBannedLength = 10</c>), added to stop the
/// "Tasks" / <c>System.Threading.Tasks</c> false positive, also silently drops two *real*
/// single-word dimension names out of the guard's banned list entirely — "Timing" (6 chars) and
/// "Impact" (6 chars). Both are genuine framework content (constitution Art. II.4 protects them
/// exactly as much as any of the framework's other, longer dimension and level names), yet
/// neither is long enough to clear the floor.
///
/// This test reflects the production guard's own private length-floor constants (never
/// duplicating the numbers as a guess) so it can never silently drift from what the shipped
/// guard actually enforces, then runs the guard's real scan algorithm and real banned list
/// against a stand-in source file that hardcodes both strings — proving the gap end to end
/// rather than asserting it in the abstract. The stand-in file lives in a temp scratch directory
/// outside backend/src, backend/tests, and app/src, so this test never introduces an actual
/// Art. II.4 violation into the real source tree.
/// </summary>
public class FrameworkContentGuardCoverageGapTests
{
    [Fact]
    public async Task Timing_and_Impact_are_real_dimension_names_excluded_from_the_banned_list_by_the_length_floor()
    {
        var jsonPath = Path.Combine(RepoSource.RepoRootDir(), "docs", "frameworks", "ai-maturity-sdlc.v1.json");
        var definition = await FrameworkSeeder.LoadDefinitionAsync(jsonPath, CancellationToken.None);

        var dimensionNames = definition.Dimensions.Select(d => d.Name).ToList();
        Assert.Contains("Timing", dimensionNames);
        Assert.Contains("Impact", dimensionNames);

        var bannedFiltered = BuildBannedFilteredList(definition);

        // The coverage gap: both are real dimension names pulled straight from the JSON, but the
        // guard's own length floor throws them away before the scan ever runs.
        Assert.DoesNotContain(bannedFiltered, s => string.Equals(s, "Timing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bannedFiltered, s => string.Equals(s, "Impact", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Hardcoding_Timing_and_Impact_in_a_source_file_passes_the_guards_real_scan_undetected()
    {
        var jsonPath = Path.Combine(RepoSource.RepoRootDir(), "docs", "frameworks", "ai-maturity-sdlc.v1.json");
        var definition = await FrameworkSeeder.LoadDefinitionAsync(jsonPath, CancellationToken.None);
        var bannedFiltered = BuildBannedFilteredList(definition);

        // A concrete violation, exactly the shape Art. II.4 forbids: the dimension names as C#
        // literals, standing in for e.g. a hardcoded UI label or enum-like constant.
        var scratchDir = Path.Combine(Path.GetTempPath(), $"hap6-qa-guard-gap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDir);
        var scratchFile = Path.Combine(scratchDir, "HardcodedFrameworkLabels.cs");
        try
        {
            await File.WriteAllLinesAsync(scratchFile, new[]
            {
                "namespace Hap.Attacker;",
                "public static class HardcodedFrameworkLabels",
                "{",
                "    public const string DimensionOne = \"Timing\";",
                "    public const string DimensionTwo = \"Impact\";",
                "}",
            });

            var offenders = ScanFileForBannedContent(scratchFile, bannedFiltered);

            // The guard's real algorithm, run against a file that unambiguously hardcodes both
            // framework dimension names, reports zero offenders — the coverage gap is real, not
            // hypothetical.
            Assert.Empty(offenders);
        }
        finally
        {
            Directory.Delete(scratchDir, recursive: true);
        }
    }

    /// <summary>Mirrors FrameworkContentNotHardcodedTests' banned-list construction exactly,
    /// pulling the length floors by reflection from that class's own private consts so this test
    /// can never silently fall out of sync with what the shipped guard enforces.</summary>
    private static List<string> BuildBannedFilteredList(FrameworkDefinition definition)
    {
        var guardType = typeof(FrameworkContentNotHardcodedTests);
        var minMultiWord = GetPrivateConstInt(guardType, "MinMultiWordBannedLength");
        var minSingleWord = GetPrivateConstInt(guardType, "MinSingleWordBannedLength");

        var banned = new List<string> { definition.Framework };
        banned.AddRange(definition.Levels.Select(l => l.Name));
        foreach (var dimension in definition.Dimensions)
        {
            banned.Add(dimension.Name);
            banned.AddRange(dimension.Descriptors.Values);
        }

        return banned
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => s.Contains(' ') ? s.Length >= minMultiWord : s.Length >= minSingleWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetPrivateConstInt(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(
                $"Expected private const '{fieldName}' on {type.FullName} — the guard's own length floor could not be reflected.");
        return (int)field.GetRawConstantValue()!;
    }

    private static List<string> ScanFileForBannedContent(string filePath, IReadOnlyList<string> bannedFiltered)
    {
        var offenders = new List<string>();
        var lines = File.ReadAllLines(filePath);
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var phrase in bannedFiltered)
            {
                if (lines[i].Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{filePath}:{i + 1}: contains \"{phrase}\"");
                }
            }
        }

        return offenders;
    }
}
