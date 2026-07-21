namespace Hap.Infrastructure.Frameworks;

/// <summary>Wire shape of a framework definition file (e.g.
/// <c>docs/frameworks/ai-maturity-sdlc.v1.json</c>). Deserialised case-insensitively by
/// <see cref="FrameworkSeeder"/> — this is the one place a dimension name, level name, or
/// descriptor string is allowed to flow through source, because it passes through as opaque
/// data, never as a literal (constitution Art. II.4; guarded by
/// <c>Hap.Architecture.Tests.FrameworkContentNotHardcodedTests</c>). Also reused directly by
/// tests that assert seeded rows match the file, so no test needs to hardcode the content
/// either.
///
/// Deliberately has no <c>Status</c> property: the JSON's own <c>"status"</c> field (authoring
/// metadata — see QUESTIONS.md Q-011) is never consulted by the seeder, so binding it here would
/// suggest a behaviour that doesn't exist (panel round-1 advisory). If a future story wires an
/// admin activation flow that does read it, add it back deliberately at that point.</summary>
public sealed record FrameworkDefinition(
    string Framework,
    string Key,
    int Version,
    string? Owner,
    string? Source,
    List<FrameworkLevelDefinition> Levels,
    List<FrameworkDimensionDefinition> Dimensions);

public sealed record FrameworkLevelDefinition(int Level, string Name);

/// <summary>
/// <see cref="Descriptors"/> keys are the level number as a string ("0".."3") because that is
/// literally how the JSON encodes them (object keys must be strings).
/// </summary>
public sealed record FrameworkDimensionDefinition(
    string Key,
    string Name,
    Dictionary<string, string> Descriptors);
