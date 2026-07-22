using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hap.Domain.Rollups;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// The append-only guarantee for rollup snapshots (HAP-10; research D2/D4, FR-071), asserted the same two
/// ways as the audit log:
/// <list type="number">
/// <item><see cref="RollupSnapshot"/> exposes no PUBLIC setter and no mutator method — once frozen at
/// close, external code cannot change a figure or a suppression verdict (so history can never be
/// retro-exposed or silently unsuppressed);</item>
/// <item>no source file under <c>backend/src</c> mutates/deletes the <c>RollupSnapshots</c> set.</item>
/// </list>
/// The real backstop is migration #5's DB triggers (row-level UPDATE/DELETE + statement-level BEFORE
/// TRUNCATE), mirroring audit_log: the application role cannot bypass them (only the superuser test reset,
/// via session_replication_role='replica', may — to wipe fixtures). These source checks are the early
/// signal. Category=PrivacyReporting.
/// </summary>
public class RollupSnapshotAppendOnlyTests
{
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void RollupSnapshot_exposes_no_public_setter_and_no_mutator_method()
    {
        var publicSetters = typeof(RollupSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();
        Assert.True(publicSetters.Count == 0,
            "RollupSnapshot must be frozen: found public setter(s): " + string.Join(", ", publicSetters));

        // No public instance method other than the object basics and the Create factory (which is static).
        var mutators = typeof(RollupSnapshot)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // exclude property getters
            .Select(m => m.Name)
            .ToList();
        Assert.True(mutators.Count == 0,
            "RollupSnapshot must carry no mutator — the only construction path is the static Create factory; " +
            "found instance method(s): " + string.Join(", ", mutators));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void No_source_mutates_or_deletes_the_rollup_snapshot_set()
    {
        // (1) DbSet-scoped mutation: RollupSnapshots.Remove(...) / Set<RollupSnapshot>().ExecuteDelete() etc.
        var dbSetForbidden = new Regex(
            @"(RollupSnapshots|Set\s*<\s*RollupSnapshot\s*>\s*\(\s*\))\s*\.\s*" +
            @"(Remove|RemoveRange|Update|UpdateRange|ExecuteDelete|ExecuteDeleteAsync|ExecuteUpdate|ExecuteUpdateAsync)\b",
            RegexOptions.Compiled);
        var removeCallForbidden = new Regex(@"\.\s*(Remove|RemoveRange)\s*\(", RegexOptions.Compiled);
        var entityStateForbidden = new Regex(@"EntityState\s*\.\s*(Deleted|Modified)", RegexOptions.Compiled);
        var namesRollupIdentifier = new Regex(@"(?i)rollupsnapshot", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in RepoSource.CsFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var namesRollup = namesRollupIdentifier.IsMatch(line);
                var offends =
                    dbSetForbidden.IsMatch(line)
                    || (namesRollup && removeCallForbidden.IsMatch(line))
                    || (namesRollup && entityStateForbidden.IsMatch(line));
                if (offends)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Rollup snapshots are append-only — no code may mutate or delete them. Offending lines:\n" +
            string.Join("\n", offenders));
    }
}
