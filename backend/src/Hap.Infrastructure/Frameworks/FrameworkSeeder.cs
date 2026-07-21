using System.Text.Json;
using Hap.Domain.Frameworks;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Frameworks;

/// <summary>Counts from one seed run, for the endpoint response and test assertions.</summary>
public sealed record FrameworkSeedResult(
    Guid FrameworkId,
    Guid VersionId,
    int VersionNumber,
    bool VersionCreated,
    int DimensionsCreated,
    int DescriptorsCreated);

/// <summary>
/// Loads a framework definition JSON (FR-001; e.g.
/// <c>docs/frameworks/ai-maturity-sdlc.v1.json</c>) into the Framework/FrameworkVersion/
/// Dimension/LevelDescriptor tables. Mirrors <see cref="Directory.DirectoryImportService"/>'s
/// shape: one transaction, upsert by natural key, safe to re-run.
/// <list type="bullet">
/// <item><b>Idempotent</b> — the framework upserts by <c>Key</c>, the version by
/// <c>(FrameworkId, VersionNumber)</c>; content (dimensions/descriptors) is built only the
/// first time a given version is seen, so re-seeding an already-seeded version is a no-op.</item>
/// <item><b>Bootstrap-activates</b> — if the framework has no <see cref="FrameworkVersionStatus.Active"/>
/// version yet, the newly-seeded version is activated so <c>GET /api/frameworks/current</c> has
/// something to serve from the moment of first seed (QUESTIONS.md Q-011, provisional). Seeding a
/// second version never auto-activates it.</item>
/// </list>
/// </summary>
public sealed class FrameworkSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HapDbContext _db;
    private readonly string _definitionPath;

    public FrameworkSeeder(HapDbContext db, string definitionPath)
    {
        _db = db;
        _definitionPath = definitionPath;
    }

    public async Task<FrameworkSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var definition = await LoadDefinitionAsync(_definitionPath, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var framework = await _db.Frameworks.SingleOrDefaultAsync(f => f.Key == definition.Key, cancellationToken);
        if (framework is null)
        {
            framework = Framework.Create(definition.Key, definition.Framework, description: null, definition.Owner);
            _db.Frameworks.Add(framework);
        }

        var version = await _db.FrameworkVersions.SingleOrDefaultAsync(
            v => v.FrameworkId == framework.Id && v.VersionNumber == definition.Version, cancellationToken);

        var versionIsNew = version is null;
        if (version is null)
        {
            version = FrameworkVersion.Create(framework.Id, definition.Version, sourceRef: _definitionPath);
            _db.FrameworkVersions.Add(version);
        }

        var dimensionsCreated = 0;
        var descriptorsCreated = 0;

        if (versionIsNew)
        {
            var levelNames = definition.Levels.ToDictionary(l => l.Level, l => l.Name);

            // FR-054 (panel round-1 B2): content is created exclusively through
            // version.AddDimension/AddLevelDescriptor, which enforce EnsureMutable() themselves —
            // this loop no longer relies on "versionIsNew implies unlocked" as the only guard.
            var displayOrder = 0;
            foreach (var dimensionDef in definition.Dimensions)
            {
                var dimension = version.AddDimension(dimensionDef.Key, dimensionDef.Name, displayOrder++);
                _db.Dimensions.Add(dimension);
                dimensionsCreated++;

                foreach (var (levelKey, descriptorText) in dimensionDef.Descriptors.OrderBy(kv => int.Parse(kv.Key)))
                {
                    var level = int.Parse(levelKey);
                    var levelName = levelNames.TryGetValue(level, out var name) ? name : $"Level {level}";
                    _db.LevelDescriptors.Add(version.AddLevelDescriptor(dimension, level, levelName, descriptorText));
                    descriptorsCreated++;
                }
            }

            // Bootstrap activation (Q-011, provisional): only when this framework has no Active
            // version at all yet. Runs against already-committed rows only — this in-memory
            // `version` is still Draft and unsaved, so it cannot match its own query.
            var hasActiveVersion = await _db.FrameworkVersions.AnyAsync(
                v => v.FrameworkId == framework.Id && v.Status == FrameworkVersionStatus.Active,
                cancellationToken);
            if (!hasActiveVersion)
            {
                version.Activate();
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new FrameworkSeedResult(
            framework.Id, version.Id, version.VersionNumber, versionIsNew, dimensionsCreated, descriptorsCreated);
    }

    /// <summary>Exposed so tests can load the same definition the seeder would, and assert
    /// seeded rows match it by content — without ever hardcoding the framework's strings
    /// themselves (constitution Art. II.4).</summary>
    public static async Task<FrameworkDefinition> LoadDefinitionAsync(
        string definitionPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(definitionPath))
        {
            throw new FileNotFoundException(
                $"Framework definition not found at '{definitionPath}'. Expected the JSON shape " +
                "seeded from docs/frameworks/ (e.g. ai-maturity-sdlc.v1.json).",
                definitionPath);
        }

        await using var stream = File.OpenRead(definitionPath);
        var definition = await JsonSerializer.DeserializeAsync<FrameworkDefinition>(
            stream, JsonOptions, cancellationToken);

        return definition
            ?? throw new InvalidOperationException($"Framework definition at '{definitionPath}' deserialised to null.");
    }
}
