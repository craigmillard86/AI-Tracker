namespace Hap.Domain.Audit;

/// <summary>
/// An immutable audit record (FR-050, FR-053). Append-only is enforced at the type level:
/// every property is get-only and set exactly once through the constructor (which EF Core
/// binds during materialisation). There is no setter, so an UPDATE cannot be expressed in
/// C#; there is no method that mutates state and none that deletes. Combined with the
/// architecture test asserting no <c>DbSet&lt;AuditLog&gt;</c> ever sees Update/Remove, the
/// log is write-once by construction.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; }

    /// <summary>UTC instant the audited action occurred.</summary>
    public DateTime At { get; }

    /// <summary>The person who performed the action (nullable for system-initiated actions).</summary>
    public Guid? ActorPersonId { get; }

    public AuditAction Action { get; }

    /// <summary>The person the action concerns, when applicable (e.g. whose data was viewed).</summary>
    public Guid? SubjectPersonId { get; }

    /// <summary>Structured context, stored as jsonb. Never null — defaults to an empty object.</summary>
    public string Detail { get; }

    /// <summary>Full-state constructor. Used both by callers (via <see cref="Create"/>) and by
    /// EF Core constructor binding — parameter names match property names by convention.</summary>
    public AuditLog(
        Guid id,
        DateTime at,
        Guid? actorPersonId,
        AuditAction action,
        Guid? subjectPersonId,
        string detail)
    {
        Id = id;
        At = at;
        ActorPersonId = actorPersonId;
        Action = action;
        SubjectPersonId = subjectPersonId;
        Detail = string.IsNullOrWhiteSpace(detail) ? "{}" : detail;
    }

    /// <summary>Creates a new audit entry stamped now (UTC) with a fresh id.</summary>
    public static AuditLog Create(
        AuditAction action,
        Guid? actorPersonId,
        Guid? subjectPersonId,
        string detail) =>
        new(Guid.NewGuid(), DateTime.UtcNow, actorPersonId, action, subjectPersonId, detail);
}
