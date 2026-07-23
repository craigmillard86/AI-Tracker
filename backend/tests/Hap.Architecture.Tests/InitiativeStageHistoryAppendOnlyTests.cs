using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hap.Domain.Register;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// The append-only guarantee for initiative stage history (FR-028; data-model.md
/// "InitiativeStageHistory": "Append-only, forward-only; no UPDATE/DELETE mapped"), asserted the same
/// two ways as <c>AuditAppendOnlyTests</c>:
/// <list type="number">
/// <item>the <see cref="InitiativeStageHistory"/> type exposes no setter — an UPDATE cannot be
/// expressed;</item>
/// <item>no source file under <c>backend/src</c> calls a mutating/deleting operation on the
/// <c>InitiativeStageHistories</c> set (Remove/Update/ExecuteDelete/ExecuteUpdate) — nothing can be
/// removed.</item>
/// </list>
/// Both are tagged <c>Category=PrivacyReporting</c> — matching the sibling append-only tests' convention
/// keeps this in the always-on regression suite, even though stage history is register data rather than
/// individual assessment data (this story's AC explicitly calls for "the same pattern as AuditLog").
///
/// <para>No DB trigger backstop for this story (see <see cref="InitiativeStageHistory"/>'s class doc) —
/// unlike <c>AuditAppendOnlyTests</c>/<c>RollupSnapshotAppendOnlyTests</c>, there is no migration-level
/// Postgres trigger enforcing this at the database layer. The EF mapping (no setters) plus this test pair
/// IS the guarantee for this L2 story; raising it to a DB-trigger backstop would be a later, L3-adjacent
/// change if the guarantee ever needs to be hardened.</para>
/// </summary>
public class InitiativeStageHistoryAppendOnlyTests
{
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void InitiativeStageHistory_type_has_no_setters()
    {
        var setters = typeof(InitiativeStageHistory)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.True(setters.Count == 0,
            "InitiativeStageHistory must be immutable/append-only; found setter(s): " + string.Join(", ", setters));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void No_source_mutates_or_deletes_the_initiative_stage_history_set()
    {
        // (1) DbSet-scoped mutation: InitiativeStageHistories.Remove(...) / Set<InitiativeStageHistory>().ExecuteDelete(...) etc.
        var dbSetForbidden = new Regex(
            @"(InitiativeStageHistories|Set\s*<\s*InitiativeStageHistory\s*>\s*\(\s*\))\s*\.\s*" +
            @"(Remove|RemoveRange|Update|UpdateRange|ExecuteDelete|ExecuteDeleteAsync|ExecuteUpdate|ExecuteUpdateAsync)\b",
            RegexOptions.Compiled);
        // (2) context.Remove(row) / RemoveRange(rows) — a mutating call on a line naming the type.
        var removeCallForbidden = new Regex(@"\.\s*(Remove|RemoveRange)\s*\(", RegexOptions.Compiled);
        // (3) Entry(row).State = EntityState.Deleted/Modified — the property-bag route.
        var entityStateForbidden = new Regex(@"EntityState\s*\.\s*(Deleted|Modified)", RegexOptions.Compiled);
        var namesIdentifier = new Regex(@"(?i)initiativestagehistor(y|ies)", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in RepoSource.CsFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var namesType = namesIdentifier.IsMatch(line);
                var offends =
                    dbSetForbidden.IsMatch(line)
                    || (namesType && removeCallForbidden.IsMatch(line))
                    || (namesType && entityStateForbidden.IsMatch(line));
                if (offends)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Initiative stage history is append-only — no code may mutate or delete it. Offending lines:\n" +
            string.Join("\n", offenders));
    }
}
