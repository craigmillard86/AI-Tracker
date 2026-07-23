using System.Collections.Concurrent;

namespace Hap.Infrastructure.Email;

/// <summary>Trivial <see cref="ISentMailLedger"/> test double — a process-lifetime set, no mailpit
/// round-trip — for fast unit/integration tests that don't need a live mailpit container.</summary>
public sealed class InMemorySentMailLedger : ISentMailLedger
{
    private readonly ConcurrentDictionary<string, byte> _sent = new(StringComparer.Ordinal);

    public Task<bool> WasAlreadySentAsync(string dedupToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sent.ContainsKey(dedupToken));

    public Task RecordSentAsync(string dedupToken, CancellationToken cancellationToken = default)
    {
        _sent[dedupToken] = 0;
        return Task.CompletedTask;
    }
}
