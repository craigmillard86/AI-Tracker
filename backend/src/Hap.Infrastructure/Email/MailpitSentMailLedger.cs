using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hap.Infrastructure.Email;

/// <summary>
/// The production <see cref="ISentMailLedger"/>: since every email this system ever sends lands in
/// mailpit ONLY (no other SMTP destination exists — see <see cref="SmtpOptions"/>), mailpit's own
/// message store IS the durable idempotence record; no separate table is needed.
/// <see cref="WasAlreadySentAsync"/> queries mailpit's REST search API
/// (<c>GET {baseUrl}/api/v1/search?query=&lt;dedup token&gt;</c>, base URL from config key
/// <c>Mailpit:ApiBaseUrl</c> — see Program.cs) and treats a non-empty result as "already sent today".
///
/// <para><b>Fails open.</b> Mailpit's search response shape is deserialized defensively
/// (<see cref="HasAtLeastOneMessage"/>): an unreachable mailpit, a non-2xx response, or an
/// unparseable/unexpected JSON shape all resolve to "not found" (i.e. "go ahead and send") rather
/// than throwing. A duplicate send on this local synthetic-data build is a minor annoyance, not a
/// privacy or correctness issue — silently blocking real notifications forever on a mailpit hiccup
/// would be the worse failure mode.</para>
/// </summary>
public sealed class MailpitSentMailLedger : ISentMailLedger
{
    private readonly HttpClient _http;
    private readonly ILogger<MailpitSentMailLedger> _logger;

    public MailpitSentMailLedger(HttpClient http, ILogger<MailpitSentMailLedger> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> WasAlreadySentAsync(string dedupToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/v1/search?query={Uri.EscapeDataString(dedupToken)}";
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Mailpit search returned {StatusCode} for dedup token {DedupToken}; treating as not-yet-sent (fail open).",
                    response.StatusCode, dedupToken);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return HasAtLeastOneMessage(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Mailpit search failed for dedup token {DedupToken}; treating as not-yet-sent (fail open).",
                dedupToken);
            return false;
        }
    }

    /// <summary>No-op: the mailpit message created by the send itself IS the ledger record.</summary>
    public Task RecordSentAsync(string dedupToken, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Loose, defensive parse of mailpit's <c>/api/v1/search</c> response. Empirically (mailpit
    /// v1.20.0) the response is a JSON object with a <c>messages</c> array plus count fields (e.g.
    /// <c>messages_count</c>/<c>total</c> depending on version) — this checks the array first and
    /// falls back to any numeric count field before giving up. Any shape this doesn't recognise, or
    /// invalid JSON, resolves to <c>false</c> (fail open) rather than throwing.
    /// </summary>
    internal static bool HasAtLeastOneMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                return messages.GetArrayLength() > 0;
            }

            foreach (var countField in new[] { "messages_count", "total", "count" })
            {
                if (root.TryGetProperty(countField, out var count) && count.ValueKind == JsonValueKind.Number)
                {
                    return count.GetInt32() > 0;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
