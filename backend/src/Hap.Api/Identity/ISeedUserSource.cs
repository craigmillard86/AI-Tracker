using System.Text.Json.Serialization;

namespace Hap.Api.Identity;

/// <summary>
/// Source of the local dev provider's role-picker list (FR-055: "seeded synthetic users covering
/// all seven roles, selectable at sign-in"). Mirrors the <c>IDirectorySource</c> port/adapter split
/// (Hap.Infrastructure.Directory) so tests can swap in a fixed list without touching the file system.
/// The shape matches exactly what <c>Hap.Synth</c>'s <c>SeedUsersFile</c> emits
/// (scripts/synth/generate.sh → backend/src/Hap.Synth/output/seed-users.json); Hap.Api does not
/// reference Hap.Synth (test-only dependency, per Hap.Api.Tests.csproj) — this is an independent,
/// structurally-identical read-side shape.
/// </summary>
public interface ISeedUserSource
{
    Task<IReadOnlyList<SeedUserRecord>> GetUsersAsync(CancellationToken cancellationToken = default);
}

/// <summary>One seeded sign-in user, as emitted by <c>Hap.Synth.SeedUser</c>. <see cref="Role"/> is
/// free text describing the fixture's intent ("Individual", "Manager", "BU Lead", "Group Leader",
/// "Portfolio Leader", "HIG Executive", "Platform Admin") — it drives the role-picker label and,
/// for the two labels that name an explicit <c>OrgRole</c>, the dev-seed grant-ensure step in
/// <see cref="LocalDevProvider"/> (QUESTIONS.md Q-013). It is never trusted as an authorization
/// decision by itself — only as the seed for a real <c>RoleGrant</c> row.</summary>
public sealed class SeedUserRecord
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("external_ref")]
    public string ExternalRef { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("bu_code")]
    public string BuCode { get; init; } = string.Empty;
}

/// <summary>The file envelope Hap.Synth emits — only <see cref="Users"/> matters to the API; the
/// seed/version metadata is additive provenance, ignored here.</summary>
public sealed class SeedUsersFile
{
    [JsonPropertyName("users")]
    public IReadOnlyList<SeedUserRecord> Users { get; init; } = Array.Empty<SeedUserRecord>();
}
