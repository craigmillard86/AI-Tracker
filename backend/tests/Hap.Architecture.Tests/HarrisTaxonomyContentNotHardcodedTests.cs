using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hap.Infrastructure.Register;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// Constitution Art. II.4 (HAP-13): the Harris taxonomy is DATA, not code — the five category display
/// names appear only in <c>docs/frameworks/harris-taxonomy.v1.json</c> and its seed output, never as
/// literals in C#/TS source. The banned-string list is loaded from the JSON itself (not hardcoded here
/// either), mirroring <see cref="FrameworkContentNotHardcodedTests"/>, so this guard never drifts from the
/// actual taxonomy and never needs updating when a name changes. Scans backend/src, backend/tests, and
/// app/src via the shared <see cref="RepoSource"/> helper.
///
/// <para><b>What is banned, and why the stage names are not.</b> The category DISPLAY names (the
/// human-readable labels seeded from the taxonomy's <c>categories[].name</c> — deliberately not quoted
/// here, since this file is itself scanned) are pure human-readable taxonomy content — they never appear
/// as a code identifier, so hardcoding one in source is exactly the Art. II.4 violation this guard catches.
/// The four Harris stage names (the <c>HarrisStage</c> enum members) are, by
/// contrast, the <see cref="Hap.Domain.Register.HarrisStage"/> enum's own member identifiers — structural
/// code exactly like <c>RagStatus</c> / <c>RiskTier</c> / <c>OrgRole</c>, which the constitution does not
/// forbid. Banning them would flag their own enum declaration (and every <c>.ToString()</c> / switch),
/// making the guard permanently red. What Art. II.4 actually protects for stages — the internal-stage →
/// Harris-stage MAPPING — is guaranteed data-not-code a different way: it lives only in the JSON's
/// <c>stageMap</c>, loaded by <see cref="HarrisTaxonomySeeder"/>; no code hardcodes a
/// <c>InitiativeStage.X ⇒ HarrisStage.Y</c> pairing (the map is always read from the seeded rows). The
/// internal-stage names (Idea, Evaluation, Pilot, …) are likewise the
/// <see cref="Hap.Domain.Register.InitiativeStage"/> enum identifiers and are not content.</para>
/// </summary>
public class HarrisTaxonomyContentNotHardcodedTests
{
    // Same differentiated length floor rationale as FrameworkContentNotHardcodedTests: multi-word phrases
    // are specific enough at a low floor; single-word entries need a higher floor to avoid colliding with
    // ordinary English words that legitimately appear in unrelated code.
    private const int MinMultiWordBannedLength = 5;
    private const int MinSingleWordBannedLength = 10;

    [Fact]
    public async Task No_harris_taxonomy_content_string_appears_in_backend_or_frontend_source()
    {
        var jsonPath = Path.Combine(RepoSource.RepoRootDir(), "docs", "frameworks", "harris-taxonomy.v1.json");
        Assert.True(File.Exists(jsonPath), $"Expected the seeded Harris taxonomy definition at '{jsonPath}'.");

        var definition = await HarrisTaxonomySeeder.LoadDefinitionAsync(jsonPath, CancellationToken.None);

        // Ban the five category display names (pure taxonomy content). The Harris stage names are enum
        // identifiers, not display content — see the class doc for why they are not banned.
        var banned = new List<string>();
        banned.AddRange(definition.Categories.Select(c => c.Name));

        var bannedFiltered = banned
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => s.Contains(' ')
                ? s.Length >= MinMultiWordBannedLength
                : s.Length >= MinSingleWordBannedLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(bannedFiltered.Count > 0, "No banned taxonomy strings were extracted from the JSON — guard cannot run.");

        var sourceFiles = RepoSource.CsFiles()
            .Concat(RepoSource.TestCsFiles())
            .Concat(RepoSource.TsFiles())
            .ToList();

        var offenders = new List<string>();
        foreach (var file in sourceFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var phrase in bannedFiltered)
                {
                    if (lines[i].Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{file}:{i + 1}: contains \"{phrase}\"");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Harris taxonomy content strings must live only in docs/frameworks/ and seed output, never as " +
            "source literals (constitution Art. II.4). Offending lines:\n" + string.Join("\n", offenders));
    }
}
