using Hap.Infrastructure;
using Hap.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hap.Api.Authorization;

/// <summary>
/// Sends the FR-057 "assessment moderated" notice to the individual — a minimal post-commit side
/// effect of <see cref="ManagerModerationService.ModerateAsync"/> (HAP-18 L3 guardrail: touch as
/// little of <c>Authorization/**</c> as possible, no change to moderation decision/authorization
/// logic). <see cref="NotifyAsync"/> is called strictly AFTER the store write has already committed,
/// and deliberately swallows any send failure (logged, not thrown) — the moderation itself already
/// succeeded; a mail outage must never turn a successful request into a failed one for the caller.
///
/// <para><b>No individual data in the email body</b> (red-team/QA target per the story's guardrails):
/// the template names only the cycle and a deep-link path — never a score, a moderated value, or any
/// other person's data. The individual's own results view (already seam-gated elsewhere) is the
/// disclosure surface; this is a notice that a review happened, nothing more.</para>
///
/// <para><b>No dedup ledger.</b> Unlike the threshold-driven jobs (FR-037/FR-061), this is an
/// event fired once per <c>Submitted → Moderated</c> transition, and that transition is forward-only
/// and single-fire by construction (<c>Assessment.Moderate</c> throws on a repeat call) — a genuine
/// duplicate notice would require this exact post-commit call being invoked twice for the SAME
/// already-committed write (e.g. a caller retry after a response was lost), which is an edge case not
/// worth a ledger for a single non-data-bearing notice.</para>
/// </summary>
public sealed class ModerationCompleteNotifier
{
    private readonly HapDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly EmailTemplateRenderer _templates;
    private readonly ILogger<ModerationCompleteNotifier> _logger;

    public ModerationCompleteNotifier(
        HapDbContext db, IEmailSender emailSender, EmailTemplateRenderer templates, ILogger<ModerationCompleteNotifier> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _templates = templates;
        _logger = logger;
    }

    public async Task NotifyAsync(Guid subjectPersonId, string cycleName, CancellationToken ct = default)
    {
        try
        {
            var person = await _db.People
                .Where(p => p.Id == subjectPersonId)
                .Select(p => new { p.Email })
                .SingleOrDefaultAsync(ct);
            if (person is null || string.IsNullOrWhiteSpace(person.Email))
            {
                return; // nothing to notify (shouldn't happen for an active participant, but never throw for it)
            }

            var (subject, body) = _templates.Render("moderation-complete.txt", new Dictionary<string, string>
            {
                ["CycleName"] = cycleName,
                ["DeepLinkPath"] = "/assessment/result",
            });
            await _emailSender.SendAsync(new EmailMessage(new[] { person.Email }, subject, body), ct);
        }
        catch (Exception ex)
        {
            // Best-effort notice only — the moderation write already committed before this was called.
            _logger.LogWarning(ex, "Moderation-complete notification failed for person {PersonId}; moderation itself already committed.", subjectPersonId);
        }
    }
}
