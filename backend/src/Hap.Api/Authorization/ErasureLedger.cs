using Hap.Domain.Audit;
using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The single source of truth for "which assessments have been retention-erased" (FR-052; HAP-12). The
/// authoritative signal is the append-only <see cref="AuditAction.RetentionErasure"/> audit ledger — never
/// the row content (a zeroed <c>SelfScore</c> is ambiguous with a genuine 0, Q-027). EVERY surface that
/// serves raw <c>AssessmentScore</c> VALUES must consult this before presenting them, so an erased datum is
/// disclosed as erased (own-data reads) or refused (cross-person moderation read), never shown as a
/// fabricated 0. Making this a single shared dependency — rather than an inline query repeated per surface —
/// is what lets the seam-boundary architecture guard enforce "no raw-score display read without an erasure
/// check" structurally (mirrors HAP-11's F2 projection), instead of relying on each author to remember.
/// </summary>
public sealed class ErasureLedger
{
    private readonly HapDbContext _db;

    public ErasureLedger(HapDbContext db) => _db = db;

    /// <summary>Whether a specific assessment has been retention-erased. Cheap indexed read
    /// (RetentionErasure rows for the subject) + an in-memory parse.</summary>
    public async Task<bool> IsErasedAsync(Guid subjectPersonId, Guid assessmentId, CancellationToken ct = default) =>
        (await ErasedAssessmentIdsAsync(subjectPersonId, ct)).Contains(assessmentId);

    /// <summary>The set of the subject's retention-erased assessment ids — the disclosure source for the
    /// right-of-access export and the per-cycle self/result reads.</summary>
    public async Task<IReadOnlySet<Guid>> ErasedAssessmentIdsAsync(Guid subjectPersonId, CancellationToken ct = default)
    {
        var details = await _db.AuditLogs
            .Where(a => a.Action == AuditAction.RetentionErasure && a.SubjectPersonId == subjectPersonId)
            .Select(a => a.Detail)
            .ToListAsync(ct);
        return ParseErasedAssessmentIds(details);
    }

    /// <summary>Every retention-erased assessment id across the whole log — the retention job's idempotency
    /// ledger (a second run skips these).</summary>
    public async Task<IReadOnlySet<Guid>> AllErasedAssessmentIdsAsync(CancellationToken ct = default)
    {
        var details = await _db.AuditLogs
            .Where(a => a.Action == AuditAction.RetentionErasure)
            .Select(a => a.Detail)
            .ToListAsync(ct);
        return ParseErasedAssessmentIds(details);
    }

    /// <summary>Parses erased assessment ids out of a batch of <c>RetentionErasure</c> <c>Detail</c> jsonb
    /// strings — each of which MUST carry a parseable <c>assessmentId</c> (the retention writer always emits
    /// one; a writer-shape test guards that). <b>Fail-CLOSED</b> (code advisory, post-QA panel): a
    /// <c>RetentionErasure</c> Detail we cannot resolve to an assessment id is a corrupt privacy ledger, not
    /// a row to silently drop — dropping it would fail OPEN on the display reads (an erased assessment whose
    /// ledger row is unparseable would then serve as genuine). So this THROWS <see cref="CorruptErasureLedgerException"/>
    /// rather than skip. Static so the writer and tests reuse the exact parse.</summary>
    public static IReadOnlySet<Guid> ParseErasedAssessmentIds(IEnumerable<string?> details)
    {
        var ids = new HashSet<Guid>();
        foreach (var detail in details)
        {
            Guid? parsed = null;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(detail);
                    if (doc.RootElement.TryGetProperty("assessmentId", out var prop)
                        && prop.ValueKind == System.Text.Json.JsonValueKind.String
                        && Guid.TryParse(prop.GetString(), out var id))
                    {
                        parsed = id;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // fall through to the fail-closed throw below
                }
            }

            if (parsed is null)
            {
                throw new CorruptErasureLedgerException(detail);
            }
            ids.Add(parsed.Value);
        }
        return ids;
    }
}

/// <summary>A <c>RetentionErasure</c> audit row carried a <c>Detail</c> from which no <c>assessmentId</c>
/// could be parsed — a corrupt privacy ledger. The erasure ledger fails CLOSED on this rather than silently
/// dropping the row (which would fail open on the display reads). Never expected: the retention writer always
/// emits <c>{ assessmentId, cycleId }</c> and a writer-shape test guards it; surfacing loudly is correct.</summary>
public sealed class CorruptErasureLedgerException : Exception
{
    public CorruptErasureLedgerException(string? detail)
        : base($"A RetentionErasure audit row has an unparseable Detail (no assessmentId): '{detail}'. " +
               "The erasure ledger fails closed rather than serve possibly-erased data as genuine.")
    {
    }
}
