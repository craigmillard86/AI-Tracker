using System.Text.Json;
using Hap.Domain.Assessments;
using Hap.Domain.Audit;
using Hap.Domain.Cycles;
using Hap.Infrastructure;
using Hap.Infrastructure.Audit;
using Hap.Infrastructure.Cycles;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Authorization;

/// <summary>
/// The manager-scope moderation workflow (contracts/api.md "Manager scope"; FR-008/009/010/011/012/063/069).
/// Lives in the visibility seam because every operation reads or writes individual assessment data across a
/// person boundary — so all data access funnels through <see cref="AssessmentReads"/> (authorisation) and
/// <see cref="IAssessmentStore"/> (the sole assessment-table query path), never around them.
///
/// <para><b>Three L3 boundaries this service holds (each proven by a Category=PrivacyReporting test):</b>
/// <list type="number">
/// <item>The review queue and the member-assessment read only ever reach the caller's own reach
/// (direct reports for a plain manager) — a subject outside the caller's chain is structurally
/// unreachable and returns 404 with NO audit row.</item>
/// <item><b>Exactly one</b> <see cref="AuditAction.IndividualView"/> row is written per successful
/// member-assessment read, and the audit write is <b>fail-closed</b> (staged + saved before the data is
/// returned; a failed audit fails the request — research D1).</item>
/// <item>A moderation write is a submission-class write: it consults
/// <see cref="Cycle.AllowsSubmission"/> with the subject's live late-override verdict
/// (<see cref="CycleService.HasLateOverrideAsync"/>) and rejects a post-close moderation with 423 unless
/// a late override exists (Q-017a / HAP-7 handoff). This is the ONLY place that obligation is discharged
/// for the moderation path, so it must not be dropped.</item>
/// </list></para>
/// </summary>
public sealed class ManagerModerationService
{
    private readonly HapDbContext _db;
    private readonly IAssessmentStore _store;
    private readonly AssessmentReads _reads;
    private readonly ChainResolver _chain;
    private readonly OrgGraphLoader _graphLoader;
    private readonly CycleService _cycles;
    private readonly IAuditWriter _audit;
    private readonly ErasureLedger _ledger;

    public ManagerModerationService(
        HapDbContext db,
        IAssessmentStore store,
        AssessmentReads reads,
        ChainResolver chain,
        OrgGraphLoader graphLoader,
        CycleService cycles,
        IAuditWriter audit,
        ErasureLedger ledger)
    {
        _db = db;
        _store = store;
        _reads = reads;
        _chain = chain;
        _graphLoader = graphLoader;
        _cycles = cycles;
        _audit = audit;
        _ledger = ledger;
    }

    /// <summary>
    /// The caller's review queue for the current cycle (contracts/api.md GET /api/team/reviews;
    /// FR-069/063/DR-0006): every active person whose REVIEWER OF RECORD is the caller — their direct
    /// employee reports plus any reports escalated up to them past a contractor (DR-0006) or departed
    /// (FR-070) direct manager — filtered to invited, non-excluded cycle participants (so a contractor
    /// report, who escalates INTO a queue as a person but never self-assesses, is not shown). Each item
    /// carries the assessment state and on-leave display flag; no score data crosses here (state + flags
    /// only), so it is not an audited individual-view. A caller who is nobody's reviewer of record gets
    /// <see cref="TeamReviewsView.IsManager"/> = false and an empty list — including a contractor manager,
    /// whose reports all escalate to their own manager.
    /// </summary>
    public async Task<TeamReviewsView> GetReviewQueueAsync(Guid callerPersonId, CancellationToken ct = default)
    {
        var cycle = await CurrentCycleAsync(ct);
        var graph = await _graphLoader.LoadAsync(ct);

        // Moderation ⊆ read (L3 red-team): a caller whose EXPLICIT grant strips individual-read capability
        // — a HIG Executive, a Platform Admin, or a Group-Viewer grant holder (FR-025 clause 2) — cannot
        // moderate anyone even where they are a chain reviewer of record, so their queue is empty. (A
        // grant-LESS hierarchy Group/Portfolio Leader is NOT capped here: with no explicit grant they
        // classify as a plain Manager and keep their DR-0005 one-hop read+moderate over a direct report.)
        // Gating the queue on read capability keeps it consistent with the member read + moderation paths
        // and stops an aggregates-only grant holder's queue leaking direct reports' names/states.
        var caller = await CallerContextAsync(callerPersonId, ct);
        var (role, _) = AssessmentReads.ClassifyReader(caller, graph);
        var (canReadIndividuals, _) = RoleScope.IndividualReadCapability(role);
        if (!canReadIndividuals)
        {
            return new TeamReviewsView(cycle.Id, cycle.Name, IsManager: false, Array.Empty<TeamReviewItemView>());
        }

        // Reviewees = active people whose reviewer of record is the caller. This is the escalation-aware
        // replacement for a literal ManagerPersonId match: a contractor/departed direct manager is
        // skipped, so the item lands in the effective employee manager's queue (DR-0006/FR-070).
        var reviewees = graph.People
            .Where(p => p.IsActive && _chain.ReviewerOfRecord(graph, p.Id) == callerPersonId)
            .Select(p => p.Id)
            .ToList();

        // Only invited, non-excluded participants have an assessment to moderate — this drops a contractor
        // report (Excluded) or a not-onboarded-BU person who escalated in as a reviewee but never
        // self-assesses, so the queue never shows a phantom NotStarted row for a non-participant.
        var queueIds = reviewees.Count == 0
            ? new List<Guid>()
            : await _db.CycleInvitations
                .Where(i => i.CycleId == cycle.Id && !i.Excluded && reviewees.Contains(i.PersonId))
                .Select(i => i.PersonId)
                .ToListAsync(ct);

        if (queueIds.Count == 0)
        {
            return new TeamReviewsView(cycle.Id, cycle.Name, IsManager: false, Array.Empty<TeamReviewItemView>());
        }

        var assessments = (await _store.GetAssessmentsForPeopleAsync(cycle.Id, queueIds, ct))
            .ToDictionary(a => a.PersonId);

        var people = await _db.People
            .Where(p => queueIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName, p.OnLeave })
            .ToDictionaryAsync(p => p.Id, ct);

        // CanModerate must match what the PUT path actually allows: submission-lock, not raw cycle state.
        // On an Open cycle every submitted report is moderatable; on a Closed cycle only those with a
        // per-report late override are — the same subject-keyed override ModerateAsync consults. Without
        // this, a post-close report WITH a granted override showed un-moderatable in the queue and the
        // late-override moderation flow was unreachable through the normal UI (code-reviewer SHOULD-FIX B).
        var overriddenPersonIds = cycle.State == CycleState.Open
            ? new HashSet<Guid>()
            : (await _db.CycleLateOverrides
                .Where(o => o.CycleId == cycle.Id && queueIds.Contains(o.PersonId))
                .Select(o => o.PersonId)
                .ToListAsync(ct)).ToHashSet();

        var items = queueIds
            .Select(personId =>
            {
                assessments.TryGetValue(personId, out var assessment);
                people.TryGetValue(personId, out var person);
                var state = assessment?.State ?? AssessmentState.NotStarted;
                return new TeamReviewItemView(
                    AssessmentId: assessment?.Id,
                    PersonId: personId,
                    DisplayName: person?.DisplayName ?? string.Empty,
                    OnLeave: person?.OnLeave ?? false,
                    State: state.ToString(),
                    // Mirrors the write path's submission lock (the PUT re-checks it authoritatively).
                    // Submitted OR AutoAdopted is moderatable: a post-close late override reopens an
                    // auto-adopted report for a real review (Q-017a × FR-068), gated by the same lock.
                    CanModerate: state is (AssessmentState.Submitted or AssessmentState.AutoAdopted)
                                 && cycle.AllowsSubmission(overriddenPersonIds.Contains(personId)));
            })
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TeamReviewsView(cycle.Id, cycle.Name, IsManager: true, items);
    }

    /// <summary>
    /// One direct report's assessment for moderation (contracts/api.md [A] GET
    /// /api/team/members/{id}/assessment; FR-008/063). Authorises via the seam read gate FIRST — a
    /// subject outside the caller's reach throws <see cref="AssessmentAccessDeniedException"/> (→404) and
    /// writes NO audit row. On a successful, authorised read of an existing assessment, stages <b>exactly
    /// one</b> <see cref="AuditAction.IndividualView"/> row and commits it BEFORE the data is returned
    /// (fail-closed). Returns null when the subject has no assessment row this cycle (→404, no audit —
    /// there is no individual data to view). Surfaces the FR-063 carry-forward default per dimension.
    /// </summary>
    public async Task<MemberAssessmentView?> GetMemberAssessmentAsync(
        Guid callerPersonId, Guid subjectPersonId, CancellationToken ct = default)
    {
        var cycle = await CurrentCycleAsync(ct);
        var graph = await _graphLoader.LoadAsync(ct);

        // Authorise BEFORE any store touch or audit write, with the caller's REAL grants (so an explicit
        // capability-stripping grant — HIG Executive / Platform Admin / Group-Viewer — is actually
        // consulted; passing Ungranted here would classify such a caller as a plain Manager and leak). A
        // denied read throws → 404, no audit row.
        var caller = await CallerContextAsync(callerPersonId, ct);
        var decision = _reads.AuthorizeIndividualRead(graph, caller, subjectPersonId);
        if (!decision.Allowed)
        {
            throw new AssessmentAccessDeniedException(callerPersonId, subjectPersonId, decision.Reason);
        }

        var current = await _store.GetAssessmentWithScoresAsync(subjectPersonId, cycle.Id, ct);
        if (current is null)
        {
            return null; // authorised, but nothing to view this cycle → 404, no audit (no data viewed)
        }

        // Retention-erasure REFUSAL on this cross-person surface (HAP-12; QA finding). On a dormant platform
        // the "current cycle" can resolve to a >3y-erased closed cycle (SeamCycleResolver falls back to the
        // most-recently-closed cycle when none is Open), whose raw scores are erasure placeholders (self→0,
        // manager/evidence→null). A manager is NOT the data subject, so rather than surface those fabricated
        // values (the B1 leak on a second [A] surface) this refuses — returns null → 404 (existence-leak),
        // no IndividualView audit row (nothing was viewed). The data SUBJECT's own disclosure happens on their
        // right-of-access export and result view (which DISCLOSE erasure). This funnels through the shared
        // ErasureLedger so the seam-boundary guard can enforce "no raw-score display read without an erasure
        // check".
        if (await _ledger.IsErasedAsync(subjectPersonId, current.Assessment.Id, ct))
        {
            return null;
        }

        var person = await _db.People
            .Where(p => p.Id == subjectPersonId)
            .Select(p => new { p.DisplayName, p.OnLeave })
            .SingleAsync(ct);

        // Fail-closed audit: exactly one IndividualView row, staged and committed BEFORE the data is
        // returned. If this SaveChanges throws (audit subsystem down), the request fails and no scores
        // are returned. Actor = the viewing manager; subject = the report.
        _audit.Record(AuditLog.Create(
            AuditAction.IndividualView,
            actorPersonId: callerPersonId,
            subjectPersonId: subjectPersonId,
            detail: JsonSerializer.Serialize(new { assessmentId = current.Assessment.Id, cycleId = cycle.Id })));
        await _db.SaveChangesAsync(ct);

        var dimensions = await DimensionDescriptorsAsync(cycle.FrameworkVersionId, ct);
        var currentByDim = current.Scores.ToDictionary(s => s.DimensionId);

        var priorByDim = await UnerasedPriorScoresAsync(cycle, subjectPersonId, ct);

        var dimensionViews = dimensions
            .Select(d =>
            {
                currentByDim.TryGetValue(d.Id, out var cur);
                priorByDim.TryGetValue(d.Id, out var prior);
                var selfScore = cur?.SelfScore ?? 0;
                var priorSelf = prior?.SelfScore;
                var priorManager = prior?.ManagerScore;
                var defaultManagerScore = CarryForwardDefault(selfScore, priorSelf, priorManager);
                return new MemberDimensionView(
                    DimensionId: d.Id,
                    Key: d.Key,
                    Name: d.Name,
                    DisplayOrder: d.DisplayOrder,
                    Levels: d.Levels,
                    SelfScore: selfScore,
                    SelfEvidence: cur?.SelfEvidence,
                    PriorSelfScore: priorSelf,
                    PriorManagerScore: priorManager,
                    ManagerScore: cur?.ManagerScore,
                    ManagerComment: cur?.ManagerComment,
                    DefaultManagerScore: defaultManagerScore,
                    // FR-009/FR-063: a carried-forward default can itself diverge ≥2 from the self-score
                    // (a sustained prior Δ≥2 moderation). The client must pre-empt the forced-comment state
                    // on the DEFAULT — an "accept all defaults" PUT with no comment on such a dimension is
                    // rejected 422, exactly as an edited Δ≥2 is, so GET and PUT agree (Q-019, strict FR-009).
                    DefaultCommentRequired:
                        Math.Abs(selfScore - defaultManagerScore) >= AssessmentScore.DivergenceCommentThreshold);
            })
            .Where(v => currentByDim.ContainsKey(v.DimensionId)) // only dimensions the report actually scored
            .ToList();

        var hasLateOverride = await _cycles.HasLateOverrideAsync(cycle.Id, subjectPersonId, ct);
        // Editable when moderatable: Submitted normally, or AutoAdopted under a post-close late override
        // (Q-017a × FR-068) — the same states the write path accepts, gated by the same submission lock.
        var editable = current.Assessment.State is (AssessmentState.Submitted or AssessmentState.AutoAdopted)
            && cycle.AllowsSubmission(hasLateOverride);

        return new MemberAssessmentView(
            AssessmentId: current.Assessment.Id,
            PersonId: subjectPersonId,
            DisplayName: person.DisplayName,
            CycleId: cycle.Id,
            CycleName: cycle.Name,
            State: current.Assessment.State.ToString(),
            OnLeave: person.OnLeave,
            Editable: editable,
            // The domain's FR-009 divergence threshold, surfaced so the client drives its
            // comment-required decision from the server rather than a duplicated client-side literal
            // (code-reviewer advisory). The per-dimension DefaultCommentRequired flag already applies it
            // to the carried default; this lets the client apply the same rule to a manager EDIT too.
            CommentThreshold: AssessmentScore.DivergenceCommentThreshold,
            Dimensions: dimensionViews);
    }

    /// <summary>
    /// Applies a manager's moderation (contracts/api.md PUT /api/team/reviews/{assessmentId};
    /// FR-008/009/010/063). Resolves the subject from the assessment id, then, in order:
    /// authorise as the subject's reviewer of record who also has individual-read capability (moderation ⊆
    /// read; →404, no audit, existence-leak convention) → consult
    /// the submission lock (→423 post-close without a late override, Q-017a) → apply the moderation
    /// atomically. Dimensions omitted from <paramref name="clientDecisions"/> are defaulted server-side
    /// (FR-063 carry-forward when a prior moderated score exists and the self-score is unchanged, else
    /// adopt the self-score). Δ≥2 without a comment → 422 (domain-enforced); a non-Submitted assessment
    /// → 409; an unknown dimension → 422. A successful moderation stages a <see cref="AuditAction.ScoreChange"/>
    /// audit row atomically (Q-018, fail-closed).
    /// </summary>
    public async Task ModerateAsync(
        Guid callerPersonId, Guid assessmentId, IReadOnlyList<ManagerScoreInput> clientDecisions, CancellationToken ct = default)
    {
        var assessment = await _store.GetByIdWithScoresAsync(assessmentId, ct);
        if (assessment is null)
        {
            throw new AssessmentNotFoundException(assessmentId);
        }

        var subjectPersonId = assessment.Assessment.PersonId;
        var graph = await _graphLoader.LoadAsync(ct);

        // Only the subject's reviewer of record who ALSO has individual-read capability may moderate
        // (moderation ⊆ read). Built with the caller's REAL grants so a chain ancestor whose explicit grant
        // strips capability (HIG Executive / Platform Admin / Group-Viewer) is denied — a denied moderation
        // is a 404 (existence-leak) with no audit row.
        var caller = await CallerContextAsync(callerPersonId, ct);
        var decision = _reads.AuthorizeModeration(graph, caller, subjectPersonId);
        if (!decision.Allowed)
        {
            throw new AssessmentAccessDeniedException(callerPersonId, subjectPersonId, decision.Reason);
        }

        // Submission lock (Q-017a): moderation is a submission-class write against the SUBJECT's
        // assessment, so the override that reopens it is the subject's (the same row the self path and
        // the manager late-override grant use). Post-close without an override → 423.
        var cycle = await _db.Cycles.SingleAsync(c => c.Id == assessment.Assessment.CycleId, ct);
        var hasLateOverride = await _cycles.HasLateOverrideAsync(cycle.Id, subjectPersonId, ct);
        if (!cycle.AllowsSubmission(hasLateOverride))
        {
            throw new AssessmentCycleLockedException(cycle.Id);
        }

        // Retention interlock (HAP-12) — erasure is PERMANENT against EVERY write path. A late override
        // (Q-022) can reopen a cycle closed >3 years ago whose raw scores were retention-erased (FR-052);
        // moderating it here would (i) silently REVERSE the erasure by writing a real ManagerScore/comment
        // back into the erased row, and (ii) desync the right-of-access export, which keys off the
        // append-only RetentionErasure ledger and would keep reporting the datum erased while a genuine new
        // value now exists — hiding real personal data from the subject's own export (FR-051). Refuse BEFORE
        // any write. This ledger check is the authoritative cross-request signal (the same source the export
        // discloses from); the transient AssessmentScore.Erased guard remains the same-unit-of-work backstop.
        if (await _ledger.IsErasedAsync(subjectPersonId, assessmentId, ct))
        {
            throw new ModerationErasedException(assessmentId);
        }

        var rowDimensions = assessment.Scores.Select(s => s.DimensionId).ToHashSet();
        var clientByDim = new Dictionary<Guid, ManagerScoreInput>();
        foreach (var d in clientDecisions)
        {
            // A client decision naming a dimension the report has no self-score for is invalid (422),
            // caught before the write so nothing partially applies.
            if (!rowDimensions.Contains(d.DimensionId))
            {
                throw new ModerationDimensionException(d.DimensionId);
            }
            clientByDim[d.DimensionId] = d; // last write wins on a duplicated dimension
        }

        // Prior-cycle scores drive the FR-063 default for any dimension the manager did not explicitly
        // decide (the "adopt self / carry forward" default the mockup pre-fills).
        var priorByDim = await UnerasedPriorScoresAsync(cycle, subjectPersonId, ct);

        var finalDecisions = assessment.Scores
            .Select(row =>
            {
                if (clientByDim.TryGetValue(row.DimensionId, out var provided))
                {
                    return provided;
                }
                priorByDim.TryGetValue(row.DimensionId, out var prior);
                var defaultScore = CarryForwardDefault(row.SelfScore, prior?.SelfScore, prior?.ManagerScore);
                return new ManagerScoreInput(row.DimensionId, defaultScore, Comment: null);
            })
            .ToList();

        var auditRow = AuditLog.Create(
            AuditAction.ScoreChange,
            actorPersonId: callerPersonId,
            subjectPersonId: subjectPersonId,
            detail: JsonSerializer.Serialize(new
            {
                assessmentId,
                cycleId = cycle.Id,
                dimensionsModerated = finalDecisions.Count,
            }));

        // Translate the domain's own exceptions into seam exceptions so the endpoint (outside the seam)
        // never references the domain assessment namespace, which the boundary guard forbids. Seam
        // exceptions thrown by the store (AssessmentNotFoundException, ModerationDimensionException)
        // propagate unchanged.
        try
        {
            await _store.ModerateAsync(assessmentId, callerPersonId, finalDecisions, auditRow, ct);
        }
        catch (AssessmentStateException)
        {
            throw new ModerationNotSubmittedException(assessmentId);
        }
        catch (AssessmentScoreErasedException)
        {
            // Backstop: the domain refused a mutator on a row erased within THIS unit of work (the ledger
            // interlock above is the primary, cross-request guard). Surface the same seam exception.
            throw new ModerationErasedException(assessmentId);
        }
        catch (ManagerCommentRequiredException ex)
        {
            throw new ModerationValidationException(ex.Message);
        }
        catch (ScoreOutOfRangeException ex)
        {
            throw new ModerationValidationException(ex.Message);
        }
    }


    /// <summary>FR-063 default: carry the prior cycle's moderated score forward when a prior moderated
    /// score exists AND the self-score is unchanged from the prior cycle; otherwise adopt the current
    /// self-score. A single source of truth so the member-read default and the moderation-write default
    /// can never diverge.</summary>
    internal static int CarryForwardDefault(int currentSelfScore, int? priorSelfScore, int? priorManagerScore) =>
        priorManagerScore is int carried && priorSelfScore is int priorSelf && priorSelf == currentSelfScore
            ? carried
            : currentSelfScore;

    /// <summary>The caller as the seam sees them, built from a FRESH read of their <c>RoleGrant</c> rows
    /// (HAP-4 A3: a BU-scoped grant flattens to a bare role string in the cookie, so the anchoring
    /// business unit must come from the database, never the claim). Passing this — rather than
    /// <see cref="CallerContext.Ungranted"/> — is what lets the seam's role-capability gate actually see
    /// that a caller is a HIG Executive / Group / Platform Admin / BU delegate and scope them accordingly;
    /// without it every caller classifies by org position alone and an above-BU leader would leak.</summary>
    private async Task<CallerContext> CallerContextAsync(Guid callerPersonId, CancellationToken ct)
    {
        var grants = await _db.RoleGrants
            .Where(g => g.PersonId == callerPersonId)
            .Select(g => new CallerGrant(g.Role, g.BusinessUnitId))
            .ToListAsync(ct);
        return grants.Count == 0
            ? CallerContext.Ungranted(callerPersonId)
            : new CallerContext(callerPersonId, grants);
    }

    // Cycle resolution is shared with SelfAssessmentService via SeamCycleResolver so the self and manager
    // paths can never drift on "which cycle is current" / "which is the prior cycle" (SHOULD-FIX A).
    private Task<Cycle> CurrentCycleAsync(CancellationToken ct) => SeamCycleResolver.CurrentCycleAsync(_db, ct);

    private Task<Guid?> PriorCycleIdAsync(Cycle current, CancellationToken ct) =>
        SeamCycleResolver.PriorCycleIdAsync(_db, current, ct);

    /// <summary>The subject's prior-cycle scores for FR-063 carry-forward, keyed by dimension — EMPTY if the
    /// prior assessment was retention-erased (FR-052), so an erased prior never seeds a fabricated
    /// carry-forward default. One shared helper for the member-view read and the moderation write so the
    /// suppression can't drift between them (post-QA domain fold-in).</summary>
    private async Task<Dictionary<Guid, AssessmentScore>> UnerasedPriorScoresAsync(
        Cycle cycle, Guid subjectPersonId, CancellationToken ct)
    {
        var priorCycleId = await PriorCycleIdAsync(cycle, ct);
        if (priorCycleId is null)
        {
            return new Dictionary<Guid, AssessmentScore>();
        }
        var prior = await _store.GetAssessmentWithScoresAsync(subjectPersonId, priorCycleId.Value, ct);
        if (prior is null || await _ledger.IsErasedAsync(subjectPersonId, prior.Assessment.Id, ct))
        {
            return new Dictionary<Guid, AssessmentScore>();
        }
        return prior.Scores.ToDictionary(s => s.DimensionId);
    }

    private async Task<IReadOnlyList<ModerationDimensionDescriptors>> DimensionDescriptorsAsync(
        Guid frameworkVersionId, CancellationToken ct)
    {
        var dimensions = await _db.Dimensions
            .Where(d => d.FrameworkVersionId == frameworkVersionId)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => new { d.Id, d.Key, d.Name, d.DisplayOrder })
            .ToListAsync(ct);

        var dimensionIds = dimensions.Select(d => d.Id).ToList();
        var descriptors = await _db.LevelDescriptors
            .Where(ld => dimensionIds.Contains(ld.DimensionId))
            .Select(ld => new { ld.DimensionId, ld.Level, ld.LevelName, ld.DescriptorText })
            .ToListAsync(ct);

        return dimensions
            .Select(d => new ModerationDimensionDescriptors(
                d.Id,
                d.Key,
                d.Name,
                d.DisplayOrder,
                descriptors
                    .Where(ld => ld.DimensionId == d.Id)
                    .OrderBy(ld => ld.Level)
                    .Select(ld => new SelfLevelDescriptor(ld.Level, ld.LevelName, ld.DescriptorText))
                    .ToList()))
            .ToList();
    }
}

/// <summary>Framework dimension + its 0–3 level descriptors (from seeded data, FR-001) — the moderation
/// score selector is driven from data exactly like the self-assessment form.</summary>
internal sealed record ModerationDimensionDescriptors(
    Guid Id, string Key, string Name, int DisplayOrder, IReadOnlyList<SelfLevelDescriptor> Levels);

/// <summary>One direct report in the review queue: their assessment state + on-leave display flag
/// (FR-069). No score data. <see cref="AssessmentId"/> is null when the report has no assessment row yet.</summary>
public sealed record TeamReviewItemView(
    Guid? AssessmentId, Guid PersonId, string DisplayName, bool OnLeave, string State, bool CanModerate);

/// <summary>The manager's review queue for the current cycle. <see cref="IsManager"/> is false (and the
/// list empty) for a caller with no active direct reports.</summary>
public sealed record TeamReviewsView(
    Guid CycleId, string CycleName, bool IsManager, IReadOnlyList<TeamReviewItemView> Reviews);

/// <summary>One dimension of a report's assessment as presented for moderation: the self score/evidence,
/// the prior-cycle self + moderated scores (FR-063 inputs), any existing moderated value, and the
/// computed carry-forward/adopt default the UI pre-fills.</summary>
public sealed record MemberDimensionView(
    Guid DimensionId,
    string Key,
    string Name,
    int DisplayOrder,
    IReadOnlyList<SelfLevelDescriptor> Levels,
    int SelfScore,
    string? SelfEvidence,
    int? PriorSelfScore,
    int? PriorManagerScore,
    int? ManagerScore,
    string? ManagerComment,
    int DefaultManagerScore,
    bool DefaultCommentRequired);

/// <summary>A report's assessment for the manager review screen. <see cref="Editable"/> is true only
/// when the assessment is Submitted AND the cycle currently accepts moderation (submission lock).</summary>
public sealed record MemberAssessmentView(
    Guid AssessmentId,
    Guid PersonId,
    string DisplayName,
    Guid CycleId,
    string CycleName,
    string State,
    bool OnLeave,
    bool Editable,
    int CommentThreshold,
    IReadOnlyList<MemberDimensionView> Dimensions);
