using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hap.Domain.Audit;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// The append-only guarantee for the audit log (FR-053), asserted two ways:
/// <list type="number">
/// <item>the <see cref="AuditLog"/> type exposes no setter — an UPDATE cannot be expressed;</item>
/// <item>no source file under <c>backend/src</c> calls a mutating/deleting operation on the
/// <c>AuditLogs</c> set (Remove/Update/ExecuteDelete/ExecuteUpdate) — nothing can be removed.</item>
/// </list>
/// Both are tagged <c>Category=PrivacyReporting</c> so they run in the always-on regression suite.
/// </summary>
public class AuditAppendOnlyTests
{
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void AuditLog_type_has_no_setters()
    {
        var setters = typeof(AuditLog)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.True(setters.Count == 0,
            "AuditLog must be immutable/append-only; found setter(s): " + string.Join(", ", setters));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void No_source_mutates_or_deletes_the_audit_set()
    {
        // This guard is an early signal, not the enforcement — the append-only guarantee is held at
        // the database layer by the audit_log trigger (migration #1). It is intentionally broadened
        // beyond DbSet-scoped calls to also flag the common evasions the trigger ultimately blocks.
        //
        // (1) DbSet-scoped mutation: AuditLogs.Remove(...) / Set<AuditLog>().ExecuteDelete(...) etc.
        var dbSetForbidden = new Regex(
            @"(AuditLogs|Set\s*<\s*AuditLog\s*>\s*\(\s*\))\s*\.\s*" +
            @"(Remove|RemoveRange|Update|UpdateRange|ExecuteDelete|ExecuteDeleteAsync|ExecuteUpdate|ExecuteUpdateAsync)\b",
            RegexOptions.Compiled);
        // (2) context.Remove(auditLog) / RemoveRange(auditLogs) — a mutating call on a line naming audit.
        var removeCallForbidden = new Regex(@"\.\s*(Remove|RemoveRange)\s*\(", RegexOptions.Compiled);
        // (3) Entry(auditLog).State = EntityState.Deleted/Modified — the property-bag route.
        var entityStateForbidden = new Regex(@"EntityState\s*\.\s*(Deleted|Modified)", RegexOptions.Compiled);
        // Case-insensitive "auditlog" (the C# identifier) but NOT the snake_case table name "audit_log",
        // so the migration's own "... ON audit_log" DDL is not mistaken for an offence.
        var namesAuditIdentifier = new Regex(@"(?i)auditlog", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in RepoSource.CsFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var namesAudit = namesAuditIdentifier.IsMatch(line);
                var offends =
                    dbSetForbidden.IsMatch(line)
                    || (namesAudit && removeCallForbidden.IsMatch(line))
                    || (namesAudit && entityStateForbidden.IsMatch(line));
                if (offends)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Audit log is append-only — no code may mutate or delete it. Offending lines:\n" +
            string.Join("\n", offenders));
    }
}
