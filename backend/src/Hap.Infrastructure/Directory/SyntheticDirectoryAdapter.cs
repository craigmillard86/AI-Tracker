using System.Text.Json;

namespace Hap.Infrastructure.Directory;

/// <summary>
/// The shipped <see cref="IDirectorySource"/> for the local build: reads the deterministic
/// JSON snapshot emitted by <c>Hap.Synth</c> (<c>scripts/synth/generate.sh</c>). It depends
/// only on the JSON contract, not on the generator project, so production infrastructure
/// carries no reference to the synthetic-data code. The Entra ID adapter (deferred) will
/// implement the same port.
/// </summary>
public sealed class SyntheticDirectoryAdapter : IDirectorySource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _snapshotPath;

    public SyntheticDirectoryAdapter(string snapshotPath) => _snapshotPath = snapshotPath;

    public async Task<DirectorySnapshot> FetchSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_snapshotPath))
        {
            throw new FileNotFoundException(
                $"Synthetic directory snapshot not found at '{_snapshotPath}'. " +
                "Generate it with scripts/synth/generate.sh (canonical seed).",
                _snapshotPath);
        }

        await using var stream = File.OpenRead(_snapshotPath);
        var snapshot = await JsonSerializer.DeserializeAsync<DirectorySnapshot>(
            stream, JsonOptions, cancellationToken);

        return snapshot
            ?? throw new InvalidOperationException(
                $"Directory snapshot at '{_snapshotPath}' deserialised to null.");
    }
}
