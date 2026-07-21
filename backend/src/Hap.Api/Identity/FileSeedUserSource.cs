using System.Text.Json;

namespace Hap.Api.Identity;

/// <summary>Reads the seed-users file produced by <c>scripts/synth/generate.sh</c>
/// (backend/src/Hap.Synth/output/seed-users.json — gitignored, regenerated deterministically).
/// Mirrors <c>SyntheticDirectoryAdapter</c>'s file-path-port pattern.</summary>
public sealed class FileSeedUserSource : ISeedUserSource
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public FileSeedUserSource(string path) => _path = path;

    public async Task<IReadOnlyList<SeedUserRecord>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException(
                $"Seed-users file not found at '{_path}'. Run scripts/synth/generate.sh first.", _path);
        }

        await using var stream = File.OpenRead(_path);
        var file = await JsonSerializer.DeserializeAsync<SeedUsersFile>(stream, Options, cancellationToken)
            ?? throw new InvalidOperationException("Seed-users JSON deserialised to null.");
        return file.Users;
    }
}
