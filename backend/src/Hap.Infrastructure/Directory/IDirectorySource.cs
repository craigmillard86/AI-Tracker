using System.Text.Json.Serialization;

namespace Hap.Infrastructure.Directory;

/// <summary>
/// The directory-source port (contracts/api.md "Ports — IDirectorySource"; constitution
/// Art. IX.4 — one of the only two ports). Returns a full snapshot; the importer diffs it
/// against stored state. This build ships the synthetic adapter; the Entra ID / Graph
/// adapter is a deferred, decision-recorded story that implements this same interface.
/// </summary>
public interface IDirectorySource
{
    Task<DirectorySnapshot> FetchSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>The snapshot shape consumed by the importer — exactly the JSON emitted by
/// <c>Hap.Synth</c> (bus[] + persons[]); the additive metadata envelope is ignored on import.</summary>
public sealed class DirectorySnapshot
{
    [JsonPropertyName("bus")]
    public IReadOnlyList<DirectoryBu> Bus { get; init; } = Array.Empty<DirectoryBu>();

    [JsonPropertyName("persons")]
    public IReadOnlyList<DirectoryPerson> Persons { get; init; } = Array.Empty<DirectoryPerson>();
}

public sealed class DirectoryBu
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; init; } = string.Empty;

    [JsonPropertyName("portfolio")]
    public string Portfolio { get; init; } = string.Empty;
}

public sealed class DirectoryPerson
{
    [JsonPropertyName("external_ref")]
    public string ExternalRef { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("job_title")]
    public string JobTitle { get; init; } = string.Empty;

    [JsonPropertyName("manager_external_ref")]
    public string? ManagerExternalRef { get; init; }

    [JsonPropertyName("bu_code")]
    public string BuCode { get; init; } = string.Empty;

    [JsonPropertyName("employee_type")]
    public string EmployeeType { get; init; } = "Employee";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; } = true;

    [JsonPropertyName("on_leave")]
    public bool OnLeave { get; init; }
}
