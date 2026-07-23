using System.Text.Json;
using Hap.Domain.Audit;
using Hap.Infrastructure;
using Hap.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// GDPR right-of-access export (contracts/api.md GET /api/me/export; FR-051; HAP-12). Lives in the
/// visibility seam because it reads individual assessment data — but it is structurally self-only: it takes
/// the caller's own <c>personId</c> as BOTH actor and subject, so there is no parameter through which one
/// person could export another's data. It returns EVERYTHING held about the caller (FR-051): profile + org
/// links, and every cycle's self + manager scores, evidence, comments, and moderation metadata — assembled
/// from the seam store (the only assessment-table query path) plus public org/framework/cycle tables.
///
/// <para><b>The export itself writes an <see cref="AuditAction.Export"/> audit row, fail-closed</b> (staged
/// and committed BEFORE the export is returned; a failed audit fails the request — mirrors the
/// <c>IndividualView</c> pattern). This is the one self-scope read that IS audited: a subject-access export
/// is a data-egress event a DPO must be able to see, distinct from the "viewing your own data is not
/// audited" rule for the ordinary assessment screens (contracts/api.md).</para>
/// </summary>
public sealed class PersonalDataExportService
{
    private readonly HapDbContext _db;
    private readonly ISelfAssessmentStore _store;
    private readonly IAuditWriter _audit;
    private readonly ErasureLedger _ledger;

    public PersonalDataExportService(HapDbContext db, ISelfAssessmentStore store, IAuditWriter audit, ErasureLedger ledger)
    {
        _db = db;
        _store = store;
        _audit = audit;
        _ledger = ledger;
    }

    /// <summary>Assembles the caller's complete personal-data export and writes the <c>Export</c> audit row
    /// before returning it. Throws <see cref="PersonNotFoundExportException"/> (→404) if the caller has no
    /// person row (a broken session), so an export never returns an empty shell for a non-existent subject.</summary>
    public async Task<PersonalDataExport> ExportAsync(Guid personId, CancellationToken ct = default)
    {
        var profile = await _db.People
            .Where(p => p.Id == personId)
            .Select(p => new
            {
                p.Id, p.ExternalRef, p.DisplayName, p.Email, p.JobTitle,
                p.EmployeeType, p.BusinessUnitId, p.ManagerPersonId, p.OnLeave, p.IsActive,
            })
            .SingleOrDefaultAsync(ct);
        if (profile is null)
        {
            throw new PersonNotFoundExportException(personId);
        }

        var businessUnitName = await _db.BusinessUnits
            .Where(b => b.Id == profile.BusinessUnitId)
            .Select(b => b.Name)
            .SingleOrDefaultAsync(ct);

        var assessments = await _store.GetAllForPersonAsync(personId, ct);

        // Which of this person's assessments have been retention-erased (FR-052)? The shared ErasureLedger —
        // the same authoritative source the retention job, the moderation interlock, and the display reads
        // consult — so the export DISCLOSES erasure rather than presenting the zeroed/nulled placeholders as
        // genuine values (B1: a fabricated self-score of 0 must never read as a real L0 self-assessment).
        var erasedAssessmentIds = await _ledger.ErasedAssessmentIdsAsync(personId, ct);

        // Cycle names and dimension identities for every referenced cycle/dimension, so the export labels
        // each figure from framework DATA rather than raw ids (FR-001). One lookup each, across all cycles.
        var cycleIds = assessments.Select(a => a.Assessment.CycleId).Distinct().ToList();
        var cycleNames = await _db.Cycles
            .Where(c => cycleIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var dimensionIds = assessments.SelectMany(a => a.Scores).Select(s => s.DimensionId).Distinct().ToList();
        var dimensions = await _db.Dimensions
            .Where(d => dimensionIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Key, d.Name, d.DisplayOrder })
            .ToDictionaryAsync(d => d.Id, ct);

        var cycleExports = assessments
            .Select(a =>
            {
                var assessment = a.Assessment;
                var dataErased = erasedAssessmentIds.Contains(assessment.Id);
                var scores = a.Scores
                    .OrderBy(s => dimensions.TryGetValue(s.DimensionId, out var d) ? d.DisplayOrder : int.MaxValue)
                    .Select(s =>
                    {
                        dimensions.TryGetValue(s.DimensionId, out var d);
                        // When the assessment has been retention-erased, disclose it: the raw values are the
                        // erased placeholders (self→0, evidence/manager→null), so present them as null (never
                        // a plausible real value) and flag Erased. Erased ≠ never-happened — the cycle and its
                        // moderation metadata still show, marked erased.
                        return dataErased
                            ? new ExportScore(
                                DimensionId: s.DimensionId,
                                DimensionKey: d?.Key,
                                DimensionName: d?.Name,
                                SelfScore: null,
                                SelfEvidence: null,
                                ManagerScore: null,
                                ManagerComment: null,
                                Erased: true)
                            : new ExportScore(
                                DimensionId: s.DimensionId,
                                DimensionKey: d?.Key,
                                DimensionName: d?.Name,
                                SelfScore: s.SelfScore,
                                SelfEvidence: s.SelfEvidence,
                                ManagerScore: s.ManagerScore,
                                ManagerComment: s.ManagerComment,
                                Erased: false);
                    })
                    .ToList();

                return new ExportCycle(
                    CycleId: assessment.CycleId,
                    CycleName: cycleNames.TryGetValue(assessment.CycleId, out var name) ? name : null,
                    State: assessment.State.ToString(),
                    SubmittedAt: assessment.SubmittedAt,
                    ModeratedAt: assessment.ModeratedAt,
                    ModeratedByPersonId: assessment.ModeratedByPersonId,
                    Unmoderated: assessment.Unmoderated,
                    DataErased: dataErased,
                    Scores: scores);
            })
            .ToList();

        var export = new PersonalDataExport(
            Person: new ExportPerson(
                PersonId: profile.Id,
                ExternalRef: profile.ExternalRef,
                DisplayName: profile.DisplayName,
                Email: profile.Email,
                JobTitle: profile.JobTitle,
                EmployeeType: profile.EmployeeType.ToString(),
                BusinessUnitId: profile.BusinessUnitId,
                BusinessUnitName: businessUnitName,
                ManagerPersonId: profile.ManagerPersonId,
                OnLeave: profile.OnLeave,
                IsActive: profile.IsActive),
            Cycles: cycleExports,
            ExportedAt: DateTime.UtcNow);

        // Fail-closed Export audit row: actor == subject == the caller. Staged and committed BEFORE the
        // export is handed back, so a down audit subsystem fails the whole request (no silent egress).
        _audit.Record(AuditLog.Create(
            AuditAction.Export,
            actorPersonId: personId,
            subjectPersonId: personId,
            detail: JsonSerializer.Serialize(new { cycles = cycleExports.Count, scoreRows = assessments.Sum(a => a.Scores.Count) })));
        await _db.SaveChangesAsync(ct);

        return export;
    }
}

/// <summary>The caller has no person row — a broken session. Mapped to 404 by the endpoint.</summary>
public sealed class PersonNotFoundExportException : Exception
{
    public PersonNotFoundExportException(Guid personId)
        : base($"No person row exists for {personId}; cannot assemble a personal-data export.")
    {
    }
}

/// <summary>The complete GDPR right-of-access export for one person (FR-051): their profile + org links and
/// every cycle's assessment data. Serialised verbatim as the response body.</summary>
public sealed record PersonalDataExport(
    ExportPerson Person,
    IReadOnlyList<ExportCycle> Cycles,
    DateTime ExportedAt);

/// <summary>The person's profile + org links in the export.</summary>
public sealed record ExportPerson(
    Guid PersonId,
    string ExternalRef,
    string DisplayName,
    string Email,
    string JobTitle,
    string EmployeeType,
    Guid BusinessUnitId,
    string? BusinessUnitName,
    Guid? ManagerPersonId,
    bool OnLeave,
    bool IsActive);

/// <summary>One cycle's assessment in the export: state + moderation metadata and the per-dimension scores.
/// <see cref="DataErased"/> is true when this cycle's raw scores were destroyed under the GDPR retention
/// policy (FR-052) — the cycle and its moderation metadata still appear (erased ≠ never-happened), but every
/// score's value is disclosed as erased.</summary>
public sealed record ExportCycle(
    Guid CycleId,
    string? CycleName,
    string State,
    DateTime? SubmittedAt,
    DateTime? ModeratedAt,
    Guid? ModeratedByPersonId,
    bool Unmoderated,
    bool DataErased,
    IReadOnlyList<ExportScore> Scores);

/// <summary>One dimension's data in the export: self score + evidence and the moderated score + comment.
/// When <see cref="Erased"/> is true (retention erasure, FR-052), every value is <c>null</c> — the raw data
/// no longer exists and is disclosed as erased, NEVER presented as a plausible real value (a fabricated 0).</summary>
public sealed record ExportScore(
    Guid DimensionId,
    string? DimensionKey,
    string? DimensionName,
    int? SelfScore,
    string? SelfEvidence,
    int? ManagerScore,
    string? ManagerComment,
    bool Erased);
