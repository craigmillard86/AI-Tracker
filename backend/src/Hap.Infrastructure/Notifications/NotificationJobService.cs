using Hap.Domain.Org;
using Hap.Domain.Register;
using Hap.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Notifications;

/// <summary>Result of one FR-037 weekly-update-discipline job run. Counts only — recipient/content
/// assertions belong to the test suite reading the recording sender / mailpit directly.</summary>
public sealed record NotificationRunResult(
    int OwnerNagsSent,
    int BuLeadEscalationsSent,
    int BuLeadEscalationsSkippedNoLead);

/// <summary>
/// FR-037 "weekly update discipline" (root spec §4.2): nags an overdue initiative's OWNER once its
/// <see cref="Initiative.LastUpdateAt"/> passes <see cref="OwnerNagThresholdDays"/> days, and
/// escalates to the initiative's BU LEAD once any initiative in that BU passes
/// <see cref="BuLeadEscalationThresholdDays"/> days — the escalation email itself lists EVERY
/// currently-overdue (&gt; <see cref="OwnerNagThresholdDays"/>d) initiative in the BU, not just the
/// one(s) that crossed the 14d trigger.
///
/// <para><b>Eligible stages</b> mirror the frontend's <c>OVERDUE_ELIGIBLE_STAGES</c> constant
/// (<c>app/src/screens/register-detail/RegisterDetailScreen.tsx</c>) — Evaluation, Pilot, Production,
/// Scaled. Idea and Retired are exempt. The two lists can't be literally shared cross-language; keep
/// them in sync by hand if the exemption rule ever changes.</para>
///
/// <para><b>Idempotence</b> goes through <see cref="ISentMailLedger"/> rather than a sent-log table
/// (a migration slot conflict with another in-flight story — see the HAP-18 story file): each dedup
/// token is a stable, per-UTC-day string embedded in the email subject
/// (<c>WUN-{initiativeId:N}-{yyyyMMdd}</c> for an owner nag, <c>WUE-{businessUnitId:N}-{yyyyMMdd}</c>
/// for a BU Lead escalation), checked before every send.</para>
///
/// <para><b>BU Lead resolution</b> goes through <see cref="IBuLeadResolver"/>, which anchors the
/// recipient on an explicit BU-scoped <c>OrgRole.BuDelegate</c> grant (the same structural anchor the
/// visibility seam uses) — NOT <c>HierarchyRoleResolver</c>'s depth-from-root label, which mislabels a
/// structurally-unrelated person as a BU's lead on a non-uniform tree (HAP-18 QA Finding 2: the escalation
/// leaked to a stranger with no relationship to the BU). The resolved candidate is then filtered through
/// <see cref="IsEligibleRecipient"/> (active, non-contractor — reader-eligibility parity with the seam's
/// <c>ReaderEligible</c>, but over plain <see cref="Person"/> attributes so this layer never imports the
/// Api-side seam; HAP-18 L3 re-review 2026-07-23). A BU with no ELIGIBLE grant-anchored lead is skipped
/// silently but counted in the result, never thrown.</para>
/// </summary>
public sealed class NotificationJobService
{
    // Mirrors app/src/screens/register-detail/RegisterDetailScreen.tsx's OVERDUE_ELIGIBLE_STAGES —
    // cannot be literally shared cross-language; keep in sync by hand if the exemption rule changes.
    private static readonly HashSet<InitiativeStage> OverdueEligibleStages = new()
    {
        InitiativeStage.Evaluation,
        InitiativeStage.Pilot,
        InitiativeStage.Production,
        InitiativeStage.Scaled,
    };

    private const int OwnerNagThresholdDays = 7;
    private const int BuLeadEscalationThresholdDays = 14;

    private readonly HapDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISentMailLedger _ledger;
    private readonly EmailTemplateRenderer _renderer;
    private readonly IBuLeadResolver _buLeadResolver;

    public NotificationJobService(
        HapDbContext db,
        IEmailSender emailSender,
        ISentMailLedger ledger,
        EmailTemplateRenderer renderer,
        IBuLeadResolver buLeadResolver)
    {
        _db = db;
        _emailSender = emailSender;
        _ledger = ledger;
        _renderer = renderer;
        _buLeadResolver = buLeadResolver;
    }

    private sealed record OverdueInitiative(Initiative Initiative, int DaysSinceUpdate);

    /// <summary><paramref name="asOf"/> defaults to now (UTC); exists so tests can pin the clock
    /// (mirrors <c>RetentionService.RunAsync</c>'s convention).</summary>
    public async Task<NotificationRunResult> RunWeeklyUpdateNagsAsync(
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = asOf ?? DateTime.UtcNow;
        var today = now.Date;

        // Register data is small at this scale (data-model.md; ~23 BUs) — load once and walk in
        // memory, matching HierarchyRoleResolver's own convention, rather than pushing the stage-set
        // filter into SQL.
        var initiatives = await _db.Initiatives.ToListAsync(ct);
        var people = await _db.People.ToDictionaryAsync(p => p.Id, ct);
        var businessUnits = await _db.BusinessUnits.ToDictionaryAsync(b => b.Id, ct);

        var overdue = initiatives
            .Where(i => OverdueEligibleStages.Contains(i.CurrentStage))
            .Select(i => new OverdueInitiative(i, DaysSince(i.LastUpdateAt, now)))
            .Where(x => x.DaysSinceUpdate > OwnerNagThresholdDays)
            .ToList();

        var ownerNagsSent = await SendOwnerNagsAsync(overdue, people, today, ct);
        var (escalationsSent, escalationsSkipped) =
            await SendBuLeadEscalationsAsync(overdue, people, businessUnits, today, ct);

        return new NotificationRunResult(ownerNagsSent, escalationsSent, escalationsSkipped);
    }

    private async Task<int> SendOwnerNagsAsync(
        IReadOnlyList<OverdueInitiative> overdue,
        IReadOnlyDictionary<Guid, Person> people,
        DateTime today,
        CancellationToken ct)
    {
        int sent = 0;
        foreach (var x in overdue)
        {
            var initiative = x.Initiative;

            var dedupToken = $"WUN-{initiative.Id:N}-{today:yyyyMMdd}";
            if (await _ledger.WasAlreadySentAsync(dedupToken, ct))
            {
                continue;
            }

            if (!people.TryGetValue(initiative.OwnerPersonId, out var owner))
            {
                // Defensive: Initiative.Create/Edit both require a non-empty OwnerPersonId, so this
                // should never happen against real data — but never throw a notification run over it.
                continue;
            }

            var (subject, body) = _renderer.Render("weekly-update-owner-nag.txt", new Dictionary<string, string>
            {
                ["InitiativeName"] = initiative.Name,
                ["DaysOverdue"] = x.DaysSinceUpdate.ToString(),
                ["DeepLinkPath"] = $"/register/{initiative.Id}",
                ["DedupToken"] = dedupToken,
            });

            await _emailSender.SendAsync(new EmailMessage(new[] { owner.Email }, subject, body), ct);
            await _ledger.RecordSentAsync(dedupToken, ct);
            sent++;
        }
        return sent;
    }

    private async Task<(int Sent, int SkippedNoLead)> SendBuLeadEscalationsAsync(
        IReadOnlyList<OverdueInitiative> overdue,
        IReadOnlyDictionary<Guid, Person> people,
        IReadOnlyDictionary<Guid, BusinessUnit> businessUnits,
        DateTime today,
        CancellationToken ct)
    {
        var buLeadMap = await _buLeadResolver.ResolveBuLeadsByBusinessUnitAsync(ct);

        var overdueByBu = overdue.GroupBy(x => x.Initiative.BusinessUnitId);

        int sent = 0;
        int skippedNoLead = 0;

        foreach (var group in overdueByBu)
        {
            var crossedEscalationThreshold = group.Any(x => x.DaysSinceUpdate > BuLeadEscalationThresholdDays);
            if (!crossedEscalationThreshold)
            {
                continue; // no initiative in this BU has crossed the 14d escalation trigger yet
            }

            var buId = group.Key;
            var dedupToken = $"WUE-{buId:N}-{today:yyyyMMdd}";
            if (await _ledger.WasAlreadySentAsync(dedupToken, ct))
            {
                continue;
            }

            if (!buLeadMap.TryGetValue(buId, out var buLeadPersonId) ||
                !people.TryGetValue(buLeadPersonId, out var buLeadPerson) ||
                !IsEligibleRecipient(buLeadPerson))
            {
                // No eligible BU-lead recipient: either no BuDelegate-anchored candidate, or one who is no
                // longer entitled. Role grants are append-only with no revocation (RoleGrant.cs), so
                // deactivation is the ONLY entitlement-ending mechanism — a departed/inactive (or contractor,
                // safe Restrictive default) delegate must not be mailed a BU escalation (HAP-18 L3 re-review
                // 2026-07-23: reader-eligibility parity with the assessment seam's ReaderEligible conjunct).
                skippedNoLead++;
                continue;
            }

            if (!businessUnits.TryGetValue(buId, out var bu))
            {
                // Defensive: BusinessUnitId is FK-enforced, so this should never happen.
                skippedNoLead++;
                continue;
            }

            var overdueList = string.Join("\n- ", group
                .OrderByDescending(x => x.DaysSinceUpdate)
                .Select(x => $"{x.Initiative.Name} ({x.DaysSinceUpdate}d)"));

            var (subject, body) = _renderer.Render(
                "weekly-update-bu-lead-escalation.txt", new Dictionary<string, string>
                {
                    ["BusinessUnitName"] = bu.Name,
                    ["OverdueInitiativesList"] = overdueList,
                    ["DedupToken"] = dedupToken,
                });

            await _emailSender.SendAsync(new EmailMessage(new[] { buLeadPerson.Email }, subject, body), ct);
            await _ledger.RecordSentAsync(dedupToken, ct);
            sent++;
        }

        return (sent, skippedNoLead);
    }

    /// <summary>
    /// Reader-eligibility conjunct for a BU-lead escalation recipient (HAP-18 L3 re-review 2026-07-23),
    /// mirroring the assessment seam's <c>AssessmentReads.ReaderEligible</c> — but expressed over PLAIN
    /// <see cref="Person"/> attributes (active, employee-type), NOT the assessment seam itself, so
    /// Hap.Infrastructure keeps its no-dependency-on-Hap.Api.Authorization layering. Active and
    /// non-contractor: the escalation carries Register/Initiative data (not Art. VI individual assessment
    /// data), but the recipient must still be a live BU curator — a departed/deactivated delegate must not
    /// keep receiving mail (grants are append-only, so deactivation is the only entitlement-ender), and a
    /// contractor is excluded under the safe Restrictive posture (constitution Art. V — uncertainty rounds
    /// up; matches the seam's Q-006 default and <c>CycleService</c>'s contractor exclusion).
    /// </summary>
    private static bool IsEligibleRecipient(Person person) =>
        person.IsActive && person.EmployeeType != EmployeeType.Contractor;

    private static int DaysSince(DateTime lastUpdateAt, DateTime now) =>
        (int)Math.Floor((now - lastUpdateAt).TotalDays);
}
