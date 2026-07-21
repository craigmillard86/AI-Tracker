using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hap.Infrastructure.Frameworks;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// Constitution Art. II.4 / HAP-6 acceptance criterion: no dimension name, level name, or
/// descriptor string appears in C#/TS source — that content is data, seeded from
/// <c>docs/frameworks/ai-maturity-sdlc.v1.json</c>, never a literal in code. The banned-string
/// list is loaded from the JSON itself (not hardcoded here either) so this guard never drifts
/// from the actual content and never needs updating when the framework's wording changes.
/// Scans backend/src, backend/tests, and app/src (panel round-1 advisory A3 — the original
/// version missed test-project source; enabling that scan surfaced a real collision — see
/// <see cref="MinSingleWordBannedLength"/>).
///
/// Carry-forward (panel round-1 advisory A2, not built in full now): matching is substring, not
/// true word-boundary/regex — a banned phrase could still collide as part of a longer unrelated
/// identifier. The differentiated length floor below is a targeted, evidence-based mitigation
/// (it fixes an actual observed false positive), not the general word-boundary solution A2
/// describes; that remains future hardening if a new short/generic phrase enters the JSON.
/// </summary>
public class FrameworkContentNotHardcodedTests
{
    // Multi-word phrases (dimension/level names, descriptor sentences) are specific enough that
    // even a low length floor rarely collides with unrelated code — this also keeps both of the
    // acceptance criteria's own named example phrases (the top-tier level's name, and the first
    // dimension's name) in scope.
    private const int MinMultiWordBannedLength = 5;

    // Single-word entries need a much higher floor: this guard was extended to scan backend/tests
    // (advisory A3) and immediately caught a real false positive — the descriptor value "Tasks"
    // matched (case-insensitively) both the local variable `tasks` and the substring "Tasks" in
    // the `System.Threading.Tasks` namespace, which appears in nearly every async C# file
    // (including this one). Ordinary English words ("Weeks", "Impact", "Timing", "Features") are
    // exactly this same risk waiting to happen; this floor keeps the guard useful rather than
    // permanently red on unrelated code.
    private const int MinSingleWordBannedLength = 10;

    [Fact]
    public async Task No_framework_content_string_appears_in_backend_or_frontend_source()
    {
        var jsonPath = Path.Combine(RepoSource.RepoRootDir(), "docs", "frameworks", "ai-maturity-sdlc.v1.json");
        Assert.True(File.Exists(jsonPath), $"Expected the seeded framework definition at '{jsonPath}'.");

        var definition = await FrameworkSeeder.LoadDefinitionAsync(jsonPath, CancellationToken.None);

        var banned = new List<string> { definition.Framework };
        banned.AddRange(definition.Levels.Select(l => l.Name));
        foreach (var dimension in definition.Dimensions)
        {
            banned.Add(dimension.Name);
            banned.AddRange(dimension.Descriptors.Values);
        }

        var bannedFiltered = banned
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => s.Contains(' ')
                ? s.Length >= MinMultiWordBannedLength
                : s.Length >= MinSingleWordBannedLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(bannedFiltered.Count > 0, "No banned content strings were extracted from the JSON — guard cannot run.");

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
                    // Case-insensitive (panel round-1 advisory A1): a case-varied copy of a
                    // banned phrase is exactly the kind of near-miss this guard exists to catch.
                    if (lines[i].Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{file}:{i + 1}: contains \"{phrase}\"");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Framework content strings must live only in docs/frameworks/ and seed output, never as " +
            "source literals (constitution Art. II.4). Offending lines:\n" + string.Join("\n", offenders));
    }
}
