using Hap.Domain.Audit;

namespace Hap.Infrastructure.Audit;

/// <summary>
/// The single write surface for the audit log. Callers stage an entry with <see cref="Record"/>;
/// it is persisted by the caller's own <c>SaveChangesAsync</c>, inside the caller's transaction,
/// so the audit row commits atomically with the business write it accompanies. That is the
/// fail-closed guarantee (research D1): if the audit write cannot be staged or persisted, the
/// surrounding unit of work rolls back and the audited action does not take effect.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Stage an audit entry on the current unit of work. Never commits on its own.</summary>
    void Record(AuditLog entry);
}

/// <summary>Default writer — stages onto the shared <see cref="HapDbContext"/>. This is the only
/// type that adds to the audit set; the architecture tests forbid any Update/Remove on it.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly HapDbContext _db;

    public AuditWriter(HapDbContext db) => _db = db;

    public void Record(AuditLog entry) => _db.AuditLogs.Add(entry);
}
