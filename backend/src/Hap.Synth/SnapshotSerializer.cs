using System.Text.Json;
using System.Text.Encodings.Web;

namespace Hap.Synth;

/// <summary>
/// Deterministic JSON serialisation for the generated artefacts. A single shared
/// options instance with pinned property order (via [JsonPropertyOrder] on the
/// models) and no naming policy means identical inputs always serialise to
/// byte-identical text (HAP-2 acceptance criterion 2). Nulls are written (never
/// omitted) so manager gaps are explicit in the output.
///
/// Newline caveat: on .NET 8 the indented writer emits the OS newline
/// (JsonWriterOptions.NewLine to override it only arrives in .NET 9), so
/// byte-identity is guaranteed per-platform — which is all criterion 2 (two runs
/// on the same machine) requires. If a cross-OS byte comparison is ever needed,
/// normalise line endings or upgrade to pin JsonWriterOptions.NewLine.
/// </summary>
public static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Relaxed encoder keeps accented synthetic names readable rather than
        // \uXXXX-escaped; deterministic either way.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SerializeSnapshot(DirectorySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static string SerializeSeedUsers(SeedUsersFile seedUsers) =>
        JsonSerializer.Serialize(seedUsers, Options);

    public static DirectorySnapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<DirectorySnapshot>(json, Options)
        ?? throw new InvalidOperationException("Snapshot JSON deserialised to null.");

    public static SeedUsersFile DeserializeSeedUsers(string json) =>
        JsonSerializer.Deserialize<SeedUsersFile>(json, Options)
        ?? throw new InvalidOperationException("Seed-users JSON deserialised to null.");
}
