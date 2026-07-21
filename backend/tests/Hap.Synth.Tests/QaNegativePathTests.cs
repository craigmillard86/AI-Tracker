using System.Diagnostics;
using System.Globalization;
using Hap.Synth;
using Xunit;

namespace Hap.Synth.Tests;

/// <summary>
/// QA-window negative-path and adversarial coverage for HAP-2 (FR-020), added
/// during the fresh-instance QA pass (CLAUDE.md §9) rather than the Dev pass.
/// Targets angles the Dev suite (<see cref="DirectoryGeneratorTests"/>) does not
/// exercise: hostile/malformed CLI arguments, PRNG boundary values, seed
/// variation actually reshaping the population (not just producing unequal
/// JSON text), management-chain cycle freedom, and independence from the host
/// machine's wall clock / locale.
/// </summary>
public sealed class QaNegativePathTests
{
    // The Hap.Synth.dll sitting next to this test assembly (copied in via the
    // ProjectReference) — resolved from the loaded type rather than a
    // hard-coded Debug/Release path, so it works under any build configuration.
    private static readonly string SynthDllPath =
        typeof(DirectoryGenerator).Assembly.Location;

    private static (int ExitCode, string StdOut, string StdErr) RunCli(
        string args, IDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{SynthDllPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi)!;
        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.ExitCode, stdOut, stdErr);
    }

    // --- CLI: malformed / hostile arguments -----------------------------------

    [Fact]
    public void Cli_NonIntegerSeed_FailsWithErrorAndNonZeroExit()
    {
        var (exitCode, _, stdErr) = RunCli("--seed not-a-number");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--seed must be an integer", stdErr);
    }

    [Fact]
    public void Cli_UnrecognisedFlag_FailsWithErrorAndNonZeroExit()
    {
        var (exitCode, _, stdErr) = RunCli("--totally-bogus-flag");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("unrecognised argument", stdErr);
    }

    [Fact]
    public void Cli_SeedFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging()
    {
        // "--seed" as the very last token: the `when i + 1 < args.Length` guard
        // fails, so it must not match the seed case and must not throw an
        // unhandled exception (e.g. index-out-of-range) — it should be reported
        // as an unrecognised/incomplete argument with a clean non-zero exit.
        var (exitCode, _, stdErr) = RunCli("--seed");

        Assert.NotEqual(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdErr));
    }

    [Fact]
    public void Cli_OutFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging()
    {
        var (exitCode, _, stdErr) = RunCli("--out");

        Assert.NotEqual(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdErr));
    }

    [Fact]
    public void Cli_SeedUsersFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging()
    {
        var (exitCode, _, stdErr) = RunCli("--seed-users");

        Assert.NotEqual(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdErr));
    }

    [Fact]
    public void Cli_SeedOverflowingLong_FailsWithErrorAndNonZeroExit()
    {
        var (exitCode, _, stdErr) = RunCli("--seed 999999999999999999999999999999");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--seed must be an integer", stdErr);
    }

    [Fact]
    public void Cli_NegativeSeed_StillGeneratesSuccessfully()
    {
        // Negative seeds are not documented as rejected; DeterministicRandom
        // casts unchecked, so a negative seed must not crash the CLI.
        string outDir = Path.Combine(Path.GetTempPath(), "hap-synth-qa-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "directory.json");
        string seedUsersPath = Path.Combine(outDir, "seed-users.json");

        try
        {
            var (exitCode, stdOut, stdErr) = RunCli(
                $"--seed -12345 --out \"{outPath}\" --seed-users \"{seedUsersPath}\"");

            Assert.Equal(0, exitCode);
            Assert.Empty(stdErr);
            Assert.True(File.Exists(outPath), stdOut);
            Assert.True(File.Exists(seedUsersPath), stdOut);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // --- PRNG boundary values --------------------------------------------------

    [Fact]
    public void DeterministicRandom_MaxLessThanMin_Throws()
    {
        var rng = new DeterministicRandom(1L);
        Assert.Throws<ArgumentException>(() => rng.Next(5, 4));
    }

    [Fact]
    public void DeterministicRandom_EqualBounds_ReturnsThatValue()
    {
        var rng = new DeterministicRandom(1L);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(7, rng.Next(7, 7));
        }
    }

    [Fact]
    public void DeterministicRandom_NextDouble_AlwaysInZeroToOneExclusive()
    {
        var rng = new DeterministicRandom(42L);
        for (int i = 0; i < 10_000; i++)
        {
            double d = rng.NextDouble();
            Assert.True(d >= 0.0 && d < 1.0, $"NextDouble produced {d} outside [0,1)");
        }
    }

    // --- Seed variation is a real distribution change, not accidental --------

    [Fact]
    public void SeedVariation_ChangesActualPersonCountNotJustJsonText()
    {
        // DirectoryGeneratorTests.Determinism_DifferentSeedProducesDifferentOutput
        // only proves the serialised JSON strings differ. That alone would pass
        // even if a bug made only cosmetic fields (e.g. name ordering) vary.
        // Prove the underlying population actually reshapes: total headcount
        // and the ordinary-BU per-BU counts (driven by Distributions.OrdinaryBu
        // Min/MaxPeople via the seeded RNG) must differ from the canonical run.
        var canonical = DirectoryGenerator.Generate(Distributions.CanonicalSeed);
        var alternate = DirectoryGenerator.Generate(Distributions.CanonicalSeed + 1);

        Assert.NotEqual(canonical.Snapshot.Persons.Count, alternate.Snapshot.Persons.Count);

        var canonicalCounts = canonical.Snapshot.Persons
            .GroupBy(p => p.BuCode)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var alternateCounts = alternate.Snapshot.Persons
            .GroupBy(p => p.BuCode)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        int busWithDifferentHeadcount = canonicalCounts.Keys
            .Count(bu => canonicalCounts[bu] != alternateCounts[bu]);
        Assert.True(busWithDifferentHeadcount > 0,
            "expected at least one BU's headcount to differ between seeds");
    }

    [Fact]
    public void SeedVariation_EngineeredEdgeCasesSurviveRegardlessOfSeed()
    {
        // The flip side of the above: the engineered fixtures are pinned by
        // external_ref, not by chance, so they must be present identically
        // under a different seed too (otherwise "determinism" is coincidental
        // on the canonical seed alone).
        var alternate = DirectoryGenerator.Generate(Distributions.CanonicalSeed + 1);
        var byRef = alternate.Snapshot.Persons.ToDictionary(p => p.ExternalRef, StringComparer.Ordinal);

        Assert.True(byRef.ContainsKey(Distributions.NullManagerRef));
        Assert.Null(byRef[Distributions.NullManagerRef].ManagerExternalRef);
        Assert.True(byRef.ContainsKey(Distributions.LeaverRef));
        Assert.False(byRef[Distributions.LeaverRef].IsActive);
        Assert.Equal(3,
            alternate.Snapshot.Persons.Count(p => p.BuCode == Distributions.SubFourBuCode));
    }

    // --- Management-chain graph integrity --------------------------------------

    [Fact]
    public void Integrity_ManagementChainHasNoCycles()
    {
        var canonical = DirectoryGenerator.Generate(Distributions.CanonicalSeed);
        var byRef = canonical.Snapshot.Persons.ToDictionary(p => p.ExternalRef, StringComparer.Ordinal);

        foreach (var person in canonical.Snapshot.Persons)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = person.ExternalRef;
            int hops = 0;

            while (current is not null)
            {
                Assert.True(visited.Add(current),
                    $"cycle detected in management chain starting at {person.ExternalRef} (revisited {current})");
                Assert.True(++hops <= canonical.Snapshot.Persons.Count,
                    $"management chain from {person.ExternalRef} exceeded population size without terminating");

                current = byRef.TryGetValue(current, out var node) ? node.ManagerExternalRef : null;
            }
        }
    }

    // --- Locale independence ----------------------------------------------------

    [Fact]
    public void Generation_IsIndependentOfThreadCulture()
    {
        string canonicalJson = SnapshotSerializer.SerializeSnapshot(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed).Snapshot);

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Arabic (Saudi Arabia) — non-Gregorian default calendar and a
            // decimal/digit convention as different as practical from
            // en-US/invariant, to surface any accidental culture-sensitive
            // formatting (dates, numbers) in the generator or serializer.
            var hostile = new CultureInfo("ar-SA");
            CultureInfo.CurrentCulture = hostile;
            CultureInfo.CurrentUICulture = hostile;

            string underHostileCulture = SnapshotSerializer.SerializeSnapshot(
                DirectoryGenerator.Generate(Distributions.CanonicalSeed).Snapshot);

            Assert.Equal(canonicalJson, underHostileCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    // --- Wall-clock / timezone independence (CLI-level) -------------------------

    [Fact]
    public void Cli_OutputIsByteIdenticalAcrossDifferentTimeZones()
    {
        string outDirA = Path.Combine(Path.GetTempPath(), "hap-synth-qa-tz-a-" + Guid.NewGuid());
        string outDirB = Path.Combine(Path.GetTempPath(), "hap-synth-qa-tz-b-" + Guid.NewGuid());
        Directory.CreateDirectory(outDirA);
        Directory.CreateDirectory(outDirB);
        string outA = Path.Combine(outDirA, "directory.json");
        string usersA = Path.Combine(outDirA, "seed-users.json");
        string outB = Path.Combine(outDirB, "directory.json");
        string usersB = Path.Combine(outDirB, "seed-users.json");

        try
        {
            var runA = RunCli(
                $"--out \"{outA}\" --seed-users \"{usersA}\"",
                new Dictionary<string, string?> { ["TZ"] = "UTC" });
            var runB = RunCli(
                // Pacific/Kiritimati: UTC+14, the furthest-ahead civil timezone —
                // maximises the chance of surfacing any local-clock dependency.
                $"--out \"{outB}\" --seed-users \"{usersB}\"",
                new Dictionary<string, string?> { ["TZ"] = "Pacific/Kiritimati" });

            Assert.Equal(0, runA.ExitCode);
            Assert.Equal(0, runB.ExitCode);

            string jsonA = File.ReadAllText(outA);
            string jsonB = File.ReadAllText(outB);
            Assert.Equal(jsonA, jsonB);

            string usersJsonA = File.ReadAllText(usersA);
            string usersJsonB = File.ReadAllText(usersB);
            Assert.Equal(usersJsonA, usersJsonB);
        }
        finally
        {
            Directory.Delete(outDirA, recursive: true);
            Directory.Delete(outDirB, recursive: true);
        }
    }
}
