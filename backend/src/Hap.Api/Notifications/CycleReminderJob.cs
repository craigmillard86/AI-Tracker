using Hap.Api.Authorization;
using Hap.Domain.Cycles;
using Hap.Infrastructure;
using Hap.Infrastructure.Email;
using Hap.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hap.Api.Notifications;

/// <summary>Counts from one FR-061 cycle-reminder run — per notification type, for the admin-run
/// wire contract. Recipient/content assertions belong to the test suite reading the recording sender /
/// mailpit directly.</summary>
public sealed record CycleReminderRunResult(
    int NonResponderRemindersSent,
    int ManagerEscalationsSent,
    int BuLeadSummariesSent,
    int BuLeadSummariesSkippedNoLead);

/// <summary>
/// FR-061 cycle reminders and escalations for an Open cycle (root spec §4.2; contracts/api.md admin
/// run endpoint; research D7). Three notifications, cadenced by days-before-(intended-)close
/// (<see cref="NotificationCadenceOptions"/>, Q-031):
/// <list type="number">
/// <item><b>Non-responder reminder</b> (default 7/3/1 days before close): each invited, non-excluded
/// participant who has NOT yet reached a responded state receives a personal reminder + deep link.
/// A participant who has submitted receives nothing.</item>
/// <item><b>Manager escalation</b> (default from 3 days before close): each reviewer of record receives
/// their team's incomplete list (the non-responders whose reviewer of record they are), BY NAME — the
/// same state-only, no-score data the review queue already shows a manager. Gated on the reviewer
/// holding individual-read CAPABILITY, exactly as <c>ManagerModerationService.GetReviewQueueAsync</c> is
/// (a capability-stripped explicit-grant holder — HIG Executive / Platform Admin / Group-Viewer — who is
/// a reviewer of record gets NO escalation, so a report's participation status never reaches an
/// aggregates-only role).</item>
/// <item><b>BU-lead summary</b> (default from 3 days before close): each BU lead receives a per-team
/// summary of their BU — team (by manager) + a COUNT of outstanding people, naming NO individuals.</item>
/// </list>
///
/// <para><b>L3 guardrails.</b> The per-person completion-state read goes ONLY through the sanctioned
/// state-only seam method <see cref="IAssessmentStore.GetNonResponderPersonIdsAsync"/> (person ids only,
/// no scores) — never a raw or parallel query over the score tables. NO email body ever carries a score,
/// a moderated value, or another person's data: reminders state only "you have not submitted"; manager
/// escalations name a manager's own reports' submission status (as the review queue does); BU-lead
/// summaries carry per-team counts only. NO migration — idempotence is a computed threshold plus a
/// per-(recipient, cycle, threshold, day) dedup token embedded in the subject and checked against the
/// <see cref="ISentMailLedger"/> (mailpit's own store in production), mirroring
/// <c>NotificationJobService</c> (FR-037).</para>
///
/// <para><b>BU-lead resolution</b> reuses the <see cref="IBuLeadResolver"/> port (FR-037) as a candidate
/// PRODUCER, anchoring on an explicit BU-scoped <c>OrgRole.BuDelegate</c> grant (HAP-18 QA Finding 1: the
/// earlier depth-from-root label mislabeled a structurally-unrelated person as a BU's lead, leaking the
/// summary to a stranger). That anchor is only ONE conjunct of the seam's read predicate, so each candidate
/// is then passed through <see cref="BuLeadPassesSeamReadGate"/> — the ONE seam authority
/// (<see cref="AssessmentReads.AuthorizeIndividualRead"/> over the candidate's full grant set) — which
/// additionally enforces grant PRECEDENCE (a co-held HigExec/PlatformAdmin/GroupViewer grant strips
/// capability) and reader ELIGIBILITY (active, non-contractor). The layering: the Infrastructure resolver
/// stays a candidate producer (it cannot depend on the Api-layer seam), and the seam-parity gate lives here
/// in the Api consumer, exactly where the manager-escalation gate already does (L3 re-review 2026-07-23). A
/// BU with no seam-entitled lead is skipped silently but counted, never thrown.</para>
/// </summary>
public sealed class CycleReminderJob
{
    private const string NonResponderTemplate = "cycle-reminder-nonresponder.txt";
    private const string ManagerEscalationTemplate = "cycle-reminder-manager-escalation.txt";
    private const string BuLeadSummaryTemplate = "cycle-reminder-bu-lead-summary.txt";

    private readonly HapDbContext _db;
    private readonly IAssessmentStore _store;
    private readonly OrgGraphLoader _graphLoader;
    private readonly ChainResolver _chain;
    private readonly AssessmentReads _reads;
    private readonly IBuLeadResolver _buLeadResolver;
    private readonly IEmailSender _emailSender;
    private readonly ISentMailLedger _ledger;
    private readonly EmailTemplateRenderer _renderer;
    private readonly NotificationCadenceOptions _cadence;

    public CycleReminderJob(
        HapDbContext db,
        IAssessmentStore store,
        OrgGraphLoader graphLoader,
        ChainResolver chain,
        AssessmentReads reads,
        IBuLeadResolver buLeadResolver,
        IEmailSender emailSender,
        ISentMailLedger ledger,
        EmailTemplateRenderer renderer,
        IOptions<NotificationCadenceOptions> cadence)
    {
        _db = db;
        _store = store;
        _graphLoader = graphLoader;
        _chain = chain;
        _reads = reads;
        _buLeadResolver = buLeadResolver;
        _emailSender = emailSender;
        _ledger = ledger;
        _renderer = renderer;
        _cadence = cadence.Value;
    }

    /// <summary><paramref name="asOf"/> defaults to now (UTC); exists so tests can pin the clock to a
    /// precise days-before-close boundary (mirrors <c>NotificationJobService</c>'s convention).</summary>
    public async Task<CycleReminderRunResult> RunAsync(DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = asOf ?? DateTime.UtcNow;
        var none = new CycleReminderRunResult(0, 0, 0, 0);

        // Only an OPEN cycle has non-responders to remind before its close (a Closed cycle is past its
        // reminder window; a Draft one has no participants yet).
        var cycle = await _db.Cycles
            .Where(c => c.State == CycleState.Open)
            .OrderByDescending(c => c.OpensAt)
            .FirstOrDefaultAsync(ct);
        if (cycle is null || cycle.OpensAt is null)
        {
            return none;
        }

        // Intended close = OpensAt + configured cycle length (Q-031: config, not a stored column).
        var intendedClose = cycle.OpensAt.Value.AddDays(_cadence.CycleLengthDays);
        var daysUntilClose = (intendedClose.Date - now.Date).Days;

        var reminderDue = _cadence.NonResponderReminderDaysBeforeClose.Contains(daysUntilClose);
        var escalationDue = _cadence.EscalationDaysBeforeClose.Contains(daysUntilClose);
        if (!reminderDue && !escalationDue)
        {
            return none; // not a configured threshold day — nothing to do
        }

        // Invited, non-excluded participants (the FR-003 invitation set; excluded contractors/not-onboarded
        // rows carry no invitation and are skipped).
        var invitedIds = await _db.CycleInvitations
            .Where(i => i.CycleId == cycle.Id && !i.Excluded)
            .Select(i => i.PersonId)
            .ToListAsync(ct);
        if (invitedIds.Count == 0)
        {
            return none;
        }

        // The sanctioned state-only seam read: who among the invited has NOT responded. Person ids only.
        var nonResponderIds = await _store.GetNonResponderPersonIdsAsync(cycle.Id, invitedIds, ct);
        if (nonResponderIds.Count == 0)
        {
            return none;
        }

        var graph = await _graphLoader.LoadAsync(ct);

        // Only chase people still ACTIVE (a leaver deactivated mid-cycle, FR-024, is not nagged).
        var nonResponders = nonResponderIds
            .Select(id => graph.Find(id))
            .Where(p => p is { IsActive: true })
            .Select(p => p!)
            .ToList();
        if (nonResponders.Count == 0)
        {
            return none;
        }

        // Reviewer of record per non-responder (null if fully detached) — drives manager escalation
        // grouping and the BU-lead per-team breakdown.
        var reviewerByPerson = nonResponders
            .ToDictionary(p => p.Id, p => _chain.ReviewerOfRecord(graph, p.Id));

        var buLeadMap = await _buLeadResolver.ResolveBuLeadsByBusinessUnitAsync(ct);
        var busWithNonResponders = nonResponders.Select(p => p.BusinessUnitId).ToHashSet();

        // Full grant set for every BU-lead CANDIDATE the resolver produced, so the seam-parity gate below
        // (BuLeadPassesSeamReadGate) can re-classify each candidate over their WHOLE grant set — the
        // resolver anchors only on a BuDelegate grant, but a co-held HigExec/PlatformAdmin/GroupViewer grant
        // strips individual-read capability (ClassifyReader precedence), and active/contractor eligibility
        // must also hold. One batched RoleGrant read, mirroring the manager-escalation gate.
        var buLeadCandidateIds = busWithNonResponders
            .Where(buLeadMap.ContainsKey)
            .Select(bu => buLeadMap[bu])
            .Distinct()
            .ToList();
        var buLeadGrants = await GrantsByPersonAsync(buLeadCandidateIds, ct);

        // One directory read for every person id an email will name or address: the non-responders, their
        // reviewers of record, and the BU leads.
        var neededIds = new HashSet<Guid>(nonResponders.Select(p => p.Id));
        foreach (var reviewer in reviewerByPerson.Values)
        {
            if (reviewer is Guid rid)
            {
                neededIds.Add(rid);
            }
        }
        foreach (var buId in busWithNonResponders)
        {
            if (buLeadMap.TryGetValue(buId, out var leadId))
            {
                neededIds.Add(leadId);
            }
        }

        var directoryRows = await _db.People
            .Where(p => neededIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName, p.Email })
            .ToListAsync(ct);
        var directory = directoryRows.ToDictionary(p => p.Id, p => new PersonRow(p.Id, p.DisplayName, p.Email));

        var buNames = await _db.BusinessUnits
            .Where(b => busWithNonResponders.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var today = now.Date;
        int reminders = 0, managerEscalations = 0, buSummaries = 0, buSkipped = 0;

        if (reminderDue)
        {
            reminders = await SendNonResponderRemindersAsync(
                cycle, daysUntilClose, nonResponders, directory, today, ct);
        }

        if (escalationDue)
        {
            managerEscalations = await SendManagerEscalationsAsync(
                cycle, daysUntilClose, nonResponders, reviewerByPerson, directory, graph, today, ct);

            (buSummaries, buSkipped) = await SendBuLeadSummariesAsync(
                cycle, daysUntilClose, nonResponders, reviewerByPerson, buLeadMap, buLeadGrants, directory, buNames, graph, today, ct);
        }

        return new CycleReminderRunResult(reminders, managerEscalations, buSummaries, buSkipped);
    }

    private async Task<int> SendNonResponderRemindersAsync(
        Cycle cycle,
        int daysUntilClose,
        IReadOnlyList<OrgPerson> nonResponders,
        IReadOnlyDictionary<Guid, PersonRow> directory,
        DateTime today,
        CancellationToken ct)
    {
        int sent = 0;
        foreach (var person in nonResponders)
        {
            if (!directory.TryGetValue(person.Id, out var row) || string.IsNullOrWhiteSpace(row.Email))
            {
                continue; // no address — never throw a run over it
            }

            var dedupToken = $"CR-{person.Id:N}-{cycle.Id:N}-T{daysUntilClose}-{today:yyyyMMdd}";
            if (await _ledger.WasAlreadySentAsync(dedupToken, ct))
            {
                continue;
            }

            var (subject, body) = _renderer.Render(NonResponderTemplate, new Dictionary<string, string>
            {
                ["CycleName"] = cycle.Name,
                ["DaysUntilClose"] = daysUntilClose.ToString(),
                ["DeepLinkPath"] = "/assessment",
                ["DedupToken"] = dedupToken,
            });

            await _emailSender.SendAsync(new EmailMessage(new[] { row.Email }, subject, body), ct);
            await _ledger.RecordSentAsync(dedupToken, ct);
            sent++;
        }
        return sent;
    }

    private async Task<int> SendManagerEscalationsAsync(
        Cycle cycle,
        int daysUntilClose,
        IReadOnlyList<OrgPerson> nonResponders,
        IReadOnlyDictionary<Guid, Guid?> reviewerByPerson,
        IReadOnlyDictionary<Guid, PersonRow> directory,
        OrgGraph graph,
        DateTime today,
        CancellationToken ct)
    {
        // Group the incomplete people by their reviewer of record — that reviewer's "team's incomplete list".
        var byReviewer = nonResponders
            .Where(p => reviewerByPerson[p.Id] is Guid)
            .GroupBy(p => reviewerByPerson[p.Id]!.Value);

        // One RoleGrant read for every candidate reviewer, so the capability gate mirrors the seam's own
        // reader classification (a capability-stripped explicit-grant reviewer is excluded, as the review
        // queue excludes them) without a per-reviewer round trip.
        var reviewerIds = byReviewer.Select(g => g.Key).ToList();
        var grantsByReviewer = await GrantsByPersonAsync(reviewerIds, ct);

        int sent = 0;
        foreach (var team in byReviewer)
        {
            var reviewerId = team.Key;
            if (!directory.TryGetValue(reviewerId, out var reviewerRow) || string.IsNullOrWhiteSpace(reviewerRow.Email))
            {
                continue;
            }

            // Moderation-queue parity: only a reviewer whose STRUCTURAL role carries individual-read
            // capability may receive their team's participation status (FR-025 clause 2 — an above-BU /
            // admin explicit-grant holder gets aggregates only, so no report names reach them here).
            if (!HasIndividualReadCapability(reviewerId, grantsByReviewer, graph))
            {
                continue;
            }

            var dedupToken = $"CE-{reviewerId:N}-{cycle.Id:N}-T{daysUntilClose}-{today:yyyyMMdd}";
            if (await _ledger.WasAlreadySentAsync(dedupToken, ct))
            {
                continue;
            }

            var incompleteNames = team
                .Select(p => directory.TryGetValue(p.Id, out var r) ? r.DisplayName : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (incompleteNames.Count == 0)
            {
                continue;
            }

            var (subject, body) = _renderer.Render(ManagerEscalationTemplate, new Dictionary<string, string>
            {
                ["CycleName"] = cycle.Name,
                ["DaysUntilClose"] = daysUntilClose.ToString(),
                ["IncompleteList"] = string.Join("\n- ", incompleteNames),
                ["DedupToken"] = dedupToken,
            });

            await _emailSender.SendAsync(new EmailMessage(new[] { reviewerRow.Email }, subject, body), ct);
            await _ledger.RecordSentAsync(dedupToken, ct);
            sent++;
        }
        return sent;
    }

    private async Task<(int Sent, int SkippedNoLead)> SendBuLeadSummariesAsync(
        Cycle cycle,
        int daysUntilClose,
        IReadOnlyList<OrgPerson> nonResponders,
        IReadOnlyDictionary<Guid, Guid?> reviewerByPerson,
        IReadOnlyDictionary<Guid, Guid> buLeadMap,
        IReadOnlyDictionary<Guid, IReadOnlyList<CallerGrant>> buLeadGrants,
        IReadOnlyDictionary<Guid, PersonRow> directory,
        IReadOnlyDictionary<Guid, string> buNames,
        OrgGraph graph,
        DateTime today,
        CancellationToken ct)
    {
        int sent = 0, skipped = 0;
        foreach (var buGroup in nonResponders.GroupBy(p => p.BusinessUnitId))
        {
            var buId = buGroup.Key;
            var dedupToken = $"CB-{buId:N}-{cycle.Id:N}-T{daysUntilClose}-{today:yyyyMMdd}";
            if (await _ledger.WasAlreadySentAsync(dedupToken, ct))
            {
                continue;
            }

            if (!buLeadMap.TryGetValue(buId, out var leadId)
                || !directory.TryGetValue(leadId, out var leadRow)
                || string.IsNullOrWhiteSpace(leadRow.Email)
                || !BuLeadPassesSeamReadGate(leadId, buGroup, buLeadGrants, graph))
            {
                // No seam-entitled lead for this BU: either no BuDelegate-anchored candidate at all, or a
                // candidate the seam would DENY a read of this BU's members (a capability-stripping co-grant,
                // an inactive/contractor delegate, or a delegate whose anchor is a different BU). Skip
                // silently but count — the existing vacant-lead fallback (FR-037 parity).
                skipped++;
                continue;
            }

            // Per-team breakdown: COUNT of outstanding people per team (team = reviewer of record). Names
            // no individual — only the team's manager and a count, so a BU lead gets a summary, not a roster.
            var perTeamLines = buGroup
                .GroupBy(p => reviewerByPerson[p.Id])
                .Select(team => new
                {
                    TeamLabel = team.Key is Guid mid && directory.TryGetValue(mid, out var mr)
                        ? $"Team led by {mr.DisplayName}"
                        : "Unassigned (no reviewer of record)",
                    Count = team.Count(),
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.TeamLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"- {x.TeamLabel}: {x.Count} not yet submitted");

            var buName = buNames.TryGetValue(buId, out var n) ? n : "your business unit";

            var (subject, body) = _renderer.Render(BuLeadSummaryTemplate, new Dictionary<string, string>
            {
                ["CycleName"] = cycle.Name,
                ["DaysUntilClose"] = daysUntilClose.ToString(),
                ["BusinessUnitName"] = buName,
                ["PerTeamSummary"] = string.Join("\n", perTeamLines),
                ["DedupToken"] = dedupToken,
            });

            await _emailSender.SendAsync(new EmailMessage(new[] { leadRow.Email }, subject, body), ct);
            await _ledger.RecordSentAsync(dedupToken, ct);
            sent++;
        }
        return (sent, skipped);
    }

    /// <summary>Batched <c>RoleGrant</c> read for a set of reviewers, shaped into the seam's caller-grant
    /// form so <see cref="AssessmentReads.ClassifyReader"/> can classify each reviewer's structural role
    /// (HAP-4 A3: a BU-scoped grant's anchoring BU must come from the row, not a cookie claim).</summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CallerGrant>>> GrantsByPersonAsync(
        IReadOnlyList<Guid> personIds, CancellationToken ct)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<CallerGrant>>();
        }

        var rows = await _db.RoleGrants
            .Where(g => personIds.Contains(g.PersonId))
            .Select(g => new { g.PersonId, g.Role, g.BusinessUnitId })
            .ToListAsync(ct);

        return rows
            .GroupBy(g => g.PersonId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CallerGrant>)g.Select(x => new CallerGrant(x.Role, x.BusinessUnitId)).ToList());
    }

    /// <summary>
    /// Seam-parity gate for a BU-lead-summary recipient (L3 re-review 2026-07-23). The resolver
    /// (<c>RoleGrantBuLeadResolver</c>) is a candidate PRODUCER that anchors only on a BU-scoped
    /// <c>OrgRole.BuDelegate</c> grant — but that anchor is just ONE conjunct of the seam's real read
    /// predicate. This binds the recipient to the ONE seam authority,
    /// <see cref="AssessmentReads.AuthorizeIndividualRead"/> evaluated over the candidate's FULL grant set,
    /// so the summary reaches the candidate ONLY if the seam would let them read that BU's individual data:
    /// <list type="bullet">
    /// <item><b>Precedence</b> — a co-held HigExecutive / PlatformAdmin / GroupViewer grant strips
    /// individual-read capability (<see cref="AssessmentReads.ClassifyReader"/> ranks those above
    /// <c>BuDelegate</c>; <c>RoleScope.cs</c>), so a dual-granted candidate is DENIED even though they hold a
    /// BuDelegate grant over this BU.</item>
    /// <item><b>Eligibility</b> — the seam's <c>ReaderEligible</c> conjunct (active, and non-contractor under
    /// the Q-006 Restrictive default) runs inside <c>AuthorizeIndividualRead</c>, so a deactivated or
    /// contractor delegate is DENIED. (Grants are append-only with no revocation, so deactivation is the only
    /// entitlement-ending mechanism until the revocation-by-superseding-record story lands.)</item>
    /// <item><b>Specific-BU anchor</b> — reach is checked against the SUBJECT's BU, so a candidate whose
    /// <c>ClassifyReader</c> anchor (the FirstOrDefault BuDelegate grant) is a DIFFERENT BU is denied for
    /// THIS BU — handling the "two BuDelegate grants over different BUs" nuance consistently with the seam,
    /// which would itself only permit them to read the anchored BU.</item>
    /// </list>
    /// The subject tested is a real non-responder in the BU (the very people the summary discloses a count
    /// about), chosen NOT to be the candidate so the own-data shortcut cannot vacuously pass. If the
    /// candidate is the SOLE non-responder in the BU the summary discloses only their own participation
    /// status (no cross-person disclosure), so self is a safe fallback.
    /// </summary>
    private bool BuLeadPassesSeamReadGate(
        Guid candidateId,
        IEnumerable<OrgPerson> buNonResponders,
        IReadOnlyDictionary<Guid, IReadOnlyList<CallerGrant>> grantsByCandidate,
        OrgGraph graph)
    {
        var caller = grantsByCandidate.TryGetValue(candidateId, out var grants) && grants.Count > 0
            ? new CallerContext(candidateId, grants)
            : CallerContext.Ungranted(candidateId);

        var subject = buNonResponders.FirstOrDefault(p => p.Id != candidateId)
            ?? buNonResponders.First();
        return _reads.AuthorizeIndividualRead(graph, caller, subject.Id).Allowed;
    }

    /// <summary>Whether <paramref name="reviewerId"/>'s structurally-derived role may reach individual
    /// data at all (FR-025 clause 2) — the same gate <c>ManagerModerationService.GetReviewQueueAsync</c>
    /// applies before showing a manager their team.</summary>
    private static bool HasIndividualReadCapability(
        Guid reviewerId,
        IReadOnlyDictionary<Guid, IReadOnlyList<CallerGrant>> grantsByReviewer,
        OrgGraph graph)
    {
        var caller = grantsByReviewer.TryGetValue(reviewerId, out var grants) && grants.Count > 0
            ? new CallerContext(reviewerId, grants)
            : CallerContext.Ungranted(reviewerId);
        var (role, _) = AssessmentReads.ClassifyReader(caller, graph);
        var (canRead, _) = RoleScope.IndividualReadCapability(role);
        return canRead;
    }

    private readonly record struct PersonRow(Guid Id, string DisplayName, string Email);
}
