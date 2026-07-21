using System.Text.Json.Serialization;

namespace Hap.Synth;

/// <summary>
/// The emitted directory snapshot. The <c>bus</c> and <c>persons</c> arrays are
/// exactly the <c>DirectorySnapshot</c> shape consumed by
/// <c>IDirectorySource.FetchSnapshotAsync</c> (contracts/api.md); <c>metadata</c>
/// is an additive provenance envelope recording the seed and generator version
/// (HAP-2 acceptance criterion 1). Property order is pinned so the JSON is stable.
/// </summary>
public sealed class DirectorySnapshot
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("metadata")]
    public required SnapshotMetadata Metadata { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("bus")]
    public required IReadOnlyList<BuRecord> Bus { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("persons")]
    public required IReadOnlyList<PersonRecord> Persons { get; init; }
}

public sealed class SnapshotMetadata
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("seed")]
    public required long Seed { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("generator_version")]
    public required string GeneratorVersion { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("person_count")]
    public required int PersonCount { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("bu_count")]
    public required int BuCount { get; init; }
}

/// <summary>A business unit. Matches the contract's <c>bus[]</c> element shape.</summary>
public sealed class BuRecord
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("group")]
    public required string Group { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("portfolio")]
    public required string Portfolio { get; init; }
}

/// <summary>A person. Matches the contract's <c>persons[]</c> element shape exactly.</summary>
public sealed class PersonRecord
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("external_ref")]
    public required string ExternalRef { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("job_title")]
    public required string JobTitle { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName("manager_external_ref")]
    public string? ManagerExternalRef { get; init; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName("bu_code")]
    public required string BuCode { get; init; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName("employee_type")]
    public required string EmployeeType { get; init; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("is_active")]
    public required bool IsActive { get; init; }

    [JsonPropertyOrder(8)]
    [JsonPropertyName("on_leave")]
    public required bool OnLeave { get; init; }
}

/// <summary>
/// One seeded sign-in user per role for the dev identity provider (FR-055).
/// External refs are stable across runs (they are fixed identities engineered
/// into the population), so later stories can hard-reference them.
/// </summary>
public sealed class SeedUsersFile
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("seed")]
    public required long Seed { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("generator_version")]
    public required string GeneratorVersion { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("users")]
    public required IReadOnlyList<SeedUser> Users { get; init; }
}

public sealed class SeedUser
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("external_ref")]
    public required string ExternalRef { get; init; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName("bu_code")]
    public required string BuCode { get; init; }
}

/// <summary>The two artefacts a generation run produces.</summary>
public sealed class GeneratedDirectory
{
    public required DirectorySnapshot Snapshot { get; init; }
    public required SeedUsersFile SeedUsers { get; init; }
}
