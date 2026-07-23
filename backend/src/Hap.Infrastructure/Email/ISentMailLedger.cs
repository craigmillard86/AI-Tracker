namespace Hap.Infrastructure.Email;

/// <summary>
/// Same-day idempotence check for outbound notification email. There is no sent-log table this
/// story (a migration slot conflict with another in-flight story — see the HAP-18 story file); every
/// dedup token is a stable, per-UTC-day string embedded directly in the sent email's subject line
/// (e.g. <c>(ref: WUN-{initiativeId:N}-{yyyyMMdd})</c>), so the durable "was this already sent today"
/// record is the outbound mail itself.
/// </summary>
public interface ISentMailLedger
{
    /// <summary>True if an email carrying <paramref name="dedupToken"/> has already gone out.</summary>
    Task<bool> WasAlreadySentAsync(string dedupToken, CancellationToken cancellationToken = default);

    /// <summary>Records that <paramref name="dedupToken"/> has now been sent. For the mailpit-backed
    /// production implementation this is a no-op — the act of sending already creates the durable
    /// record — but the call shape is uniform across implementations so callers never special-case
    /// it.</summary>
    Task RecordSentAsync(string dedupToken, CancellationToken cancellationToken = default);
}
