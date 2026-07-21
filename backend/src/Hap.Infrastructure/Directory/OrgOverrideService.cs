using System.Text.Json;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Hap.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Directory;

/// <summary>Request to record a manual org correction (FR-023).</summary>
public sealed record CreateOverrideCommand(
    Guid PersonId,
    OverrideField Field,
    string OverrideValue,
    string Reason,
    string CreatedBy);

/// <summary>
/// Writes manual org overrides (FR-023). Each write is one atomic unit: the override row, its
/// single audit row (FR-050), and the immediate application of the correction to the person all
/// commit together, or none do. The audit row is staged through <see cref="IAuditWriter"/> before
/// <c>SaveChangesAsync</c>, so a failure to record the audit fails the whole operation
/// (fail-closed). The same override is re-applied by every subsequent sync (FR-023).
///
/// The override is fully validated BEFORE anything is written: the target must resolve, must not
/// be the subject itself, and (for a manager override) must not create a management-chain cycle.
/// A rejected override therefore leaves no override row and no audit row — it also fails closed.
/// </summary>
public sealed class OrgOverrideService
{
    private readonly HapDbContext _db;
    private readonly IAuditWriter _audit;

    public OrgOverrideService(HapDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<OrgOverride>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.OrgOverrides.OrderByDescending(o => o.CreatedAt).ToListAsync(cancellationToken);

    public async Task<OrgOverride> CreateAsync(CreateOverrideCommand command, CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var person = await _db.People.FirstOrDefaultAsync(p => p.Id == command.PersonId, cancellationToken)
            ?? throw new PersonNotFoundException($"Person '{command.PersonId}' not found.");

        // Resolve and validate the target up front. Any failure throws before a single row is
        // staged, so an unsatisfiable override never leaves an override row or an audit row behind.
        var target = await ResolveAndValidateTargetAsync(person, command.Field, command.OverrideValue, cancellationToken);

        var originalValue = await ResolveOriginalValueAsync(person, command.Field, cancellationToken);

        var overrideRow = OrgOverride.Create(
            command.PersonId, command.Field, originalValue, command.OverrideValue, command.Reason, command.CreatedBy);
        _db.OrgOverrides.Add(overrideRow);

        // Exactly one audit row per override write (FR-050). Staged onto the same context/tx,
        // so if this cannot be recorded the override does not persist (fail-closed).
        _audit.Record(AuditLog.Create(
            AuditAction.OrgOverride,
            actorPersonId: null, // no identity in wave 0; set to the admin's person id once IIdentityProvider lands
            subjectPersonId: command.PersonId,
            detail: SerializeDetail(overrideRow)));

        ApplyResolved(person, command.Field, target);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return overrideRow;
    }

    /// <summary>Carries the already-resolved target of an override so it is resolved once,
    /// validated, and then applied — never re-queried or silently no-op'd.</summary>
    private sealed record ResolvedTarget(BusinessUnit? BusinessUnit, Person? Person);

    private async Task<ResolvedTarget> ResolveAndValidateTargetAsync(
        Person subject, OverrideField field, string overrideValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(overrideValue))
        {
            throw new OverrideValidationException("Override value must not be empty.");
        }

        switch (field)
        {
            case OverrideField.BusinessUnit:
                var bu = await _db.BusinessUnits.FirstOrDefaultAsync(b => b.Code == overrideValue, ct)
                    ?? throw new OverrideValidationException($"No business unit with code '{overrideValue}'.");
                return new ResolvedTarget(bu, null);

            case OverrideField.Manager:
                if (string.Equals(overrideValue, subject.ExternalRef, StringComparison.Ordinal))
                {
                    throw new OverrideValidationException("A person cannot be their own manager.");
                }

                var manager = await _db.People.FirstOrDefaultAsync(p => p.ExternalRef == overrideValue, ct)
                    ?? throw new OverrideValidationException($"No person with external ref '{overrideValue}' to set as manager.");

                if (await WouldCreateCycleAsync(subject.Id, manager.Id, ct))
                {
                    throw new OverrideValidationException(
                        $"Setting '{overrideValue}' as manager of '{subject.ExternalRef}' would create a management-chain cycle.");
                }

                return new ResolvedTarget(null, manager);

            case OverrideField.DottedLine:
                if (string.Equals(overrideValue, subject.ExternalRef, StringComparison.Ordinal))
                {
                    throw new OverrideValidationException("A person cannot have a dotted line to themselves.");
                }

                var dotted = await _db.People.FirstOrDefaultAsync(p => p.ExternalRef == overrideValue, ct)
                    ?? throw new OverrideValidationException($"No person with external ref '{overrideValue}' for the dotted line.");

                return new ResolvedTarget(null, dotted);

            default:
                throw new OverrideValidationException($"Unsupported override field '{field}'.");
        }
    }

    /// <summary>True if making <paramref name="proposedManagerId"/> the manager of
    /// <paramref name="subjectId"/> would put the subject on its own management chain. Walks the
    /// existing links up from the proposed manager; a pre-existing cycle is broken by the visited set.</summary>
    private async Task<bool> WouldCreateCycleAsync(Guid subjectId, Guid proposedManagerId, CancellationToken ct)
    {
        var links = await _db.People
            .Select(p => new { p.Id, p.ManagerPersonId })
            .ToDictionaryAsync(x => x.Id, x => x.ManagerPersonId, ct);

        var visited = new HashSet<Guid>();
        Guid? current = proposedManagerId;
        while (current is not null)
        {
            if (current.Value == subjectId)
            {
                return true;
            }

            if (!visited.Add(current.Value))
            {
                break;
            }

            current = links.TryGetValue(current.Value, out var managerId) ? managerId : null;
        }

        return false;
    }

    private async Task<string?> ResolveOriginalValueAsync(Person person, OverrideField field, CancellationToken ct) =>
        field switch
        {
            OverrideField.BusinessUnit =>
                (await _db.BusinessUnits.FirstOrDefaultAsync(b => b.Id == person.BusinessUnitId, ct))?.Code,
            OverrideField.Manager =>
                person.ManagerPersonId is null
                    ? null
                    : (await _db.People.FirstOrDefaultAsync(p => p.Id == person.ManagerPersonId, ct))?.ExternalRef,
            // A dotted line has no prior structural value to capture (it is advisory, not the manager).
            OverrideField.DottedLine => null,
            _ => null,
        };

    private static void ApplyResolved(Person person, OverrideField field, ResolvedTarget target)
    {
        switch (field)
        {
            case OverrideField.BusinessUnit:
                person.SetBusinessUnit(target.BusinessUnit!.Id);
                break;

            case OverrideField.Manager:
                person.SetManager(target.Person!.Id);
                break;

            case OverrideField.DottedLine:
                // Advisory in v1 — recorded and audited, no management-chain effect.
                break;
        }
    }

    private static string SerializeDetail(OrgOverride overrideRow) =>
        JsonSerializer.Serialize(new
        {
            overrideId = overrideRow.Id,
            field = overrideRow.Field.ToString(),
            originalValue = overrideRow.OriginalValue,
            overrideValue = overrideRow.OverrideValue,
            reason = overrideRow.Reason,
        });
}
