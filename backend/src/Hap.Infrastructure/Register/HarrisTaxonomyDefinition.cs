using System.Text.Json.Serialization;

namespace Hap.Infrastructure.Register;

/// <summary>
/// Wire shape of the Harris taxonomy definition file
/// (<c>docs/frameworks/harris-taxonomy.v1.json</c>). Deserialised by
/// <see cref="HarrisTaxonomySeeder"/> — this, like <see cref="Frameworks.FrameworkDefinition"/>, is
/// the one place the Harris category names and stage names may flow through source, because they pass
/// through as opaque DATA, never as literals (constitution Art. II.4; guarded by
/// <c>Hap.Architecture.Tests.HarrisTaxonomyContentNotHardcodedTests</c>). Reused directly by tests so
/// no test needs to hardcode the taxonomy strings either.
/// </summary>
public sealed record HarrisTaxonomyDefinition(
    [property: JsonPropertyName("taxonomy")] string Taxonomy,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("owner")] string? Owner,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("categories")] List<HarrisCategoryDefinition> Categories,
    [property: JsonPropertyName("stageMap")] List<HarrisStageMapDefinition> StageMap);

/// <summary>One Harris category row: its stable key, display name, and the two reporting flags
/// (<c>group_reported</c> — false only for "Other", FR-044; <c>customer_deployed</c> — FR-031).</summary>
public sealed record HarrisCategoryDefinition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group_reported")] bool GroupReported,
    [property: JsonPropertyName("customer_deployed")] bool CustomerDeployed);

/// <summary>One internal-stage → Harris-stage mapping row (FR-064). Both values are the enum names as
/// strings (<c>Idea</c>…<c>Retired</c> and <c>Ideation</c>…<c>IdeasTriedButStopped</c>), parsed to the
/// domain enums by the seeder.</summary>
public sealed record HarrisStageMapDefinition(
    [property: JsonPropertyName("internal_stage")] string InternalStage,
    [property: JsonPropertyName("harris_stage")] string HarrisStage);
