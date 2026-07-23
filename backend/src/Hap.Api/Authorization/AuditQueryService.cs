using Hap.Domain.Audit;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// Read-only audit search (contracts/api.md [PA] GET /api/admin/audit; FR-050/FR-053; HAP-12). The audit log
/// is append-only — enforced by the immutable <see cref="AuditLog"/> type (no setters), the architecture
/// guard forbidding any Update/Remove on the set, and the database trigger blocking UPDATE/DELETE/TRUNCATE
/// (migration #1). This service adds ONLY a read surface: it never mutates, and NO write/update/delete audit
/// endpoint exists anywhere (asserted by <c>AuditAppendOnlyTests</c> at the code level and a route-table test
/// at the API level). Platform-Admin authorisation is enforced at the endpoint (the [PA] admin group).
/// </summary>
public sealed class AuditQueryService
{
    /// <summary>Hard cap on rows returned by one search — a backstop against an unbounded scan. The most
    /// recent <see cref="MaxResults"/> matching rows are returned, newest first.</summary>
    public const int MaxResults = 500;

    private readonly HapDbContext _db;

    public AuditQueryService(HapDbContext db) => _db = db;

    /// <summary>Returns audit rows matching the optional filters — <paramref name="subjectPersonId"/> (whose
    /// data), <paramref name="action"/> (which kind of event), <paramref name="from"/> (at or after this UTC
    /// instant) — newest first, capped at <see cref="MaxResults"/>. All filters are ANDed; omitting all
    /// returns the most recent rows across the whole log.</summary>
    public async Task<IReadOnlyList<AuditRowView>> SearchAsync(
        Guid? subjectPersonId = null,
        AuditAction? action = null,
        DateTime? from = null,
        CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (subjectPersonId is Guid subject)
        {
            query = query.Where(a => a.SubjectPersonId == subject);
        }
        if (action is AuditAction act)
        {
            query = query.Where(a => a.Action == act);
        }
        if (from is DateTime fromUtc)
        {
            query = query.Where(a => a.At >= fromUtc);
        }

        return await query
            .OrderByDescending(a => a.At)
            .Take(MaxResults)
            .Select(a => new AuditRowView(
                a.Id, a.At, a.ActorPersonId, a.Action.ToString(), a.SubjectPersonId, a.Detail))
            .ToListAsync(ct);
    }
}

/// <summary>One audit row on the wire (read-only). The <see cref="Detail"/> jsonb is surfaced as-is for
/// investigation; it never contains a raw assessment score (writers keep score values out of audit detail).</summary>
public sealed record AuditRowView(
    Guid Id,
    DateTime At,
    Guid? ActorPersonId,
    string Action,
    Guid? SubjectPersonId,
    string Detail);
