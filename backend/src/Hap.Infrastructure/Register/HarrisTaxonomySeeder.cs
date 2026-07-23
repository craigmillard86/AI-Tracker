using System.Text.Json;
using Hap.Domain.Register;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Register;

/// <summary>Counts from one seed run, for the endpoint response and test assertions.</summary>
public sealed record HarrisTaxonomySeedResult(
    int CategoriesCreated,
    int CategoriesTotal,
    int StageMappingsCreated,
    int StageMappingsTotal);

/// <summary>
/// Loads the Harris taxonomy definition JSON (FR-027/FR-064; e.g.
/// <c>docs/frameworks/harris-taxonomy.v1.json</c>) into the <see cref="HarrisCategory"/> and
/// <see cref="HarrisStageMap"/> tables. Mirrors <see cref="Frameworks.FrameworkSeeder"/>'s shape: one
/// transaction, upsert by natural key, safe to re-run.
/// <list type="bullet">
/// <item><b>Idempotent</b> — categories upsert by <c>Key</c>, stage-map rows upsert by
/// <c>InternalStage</c>. A row already present is left as-is (its content is fixed configuration), so
/// re-seeding is a no-op that inserts nothing.</item>
/// <item><b>Data, not code</b> — the category/stage strings live only in the JSON and this seed
/// output (constitution Art. II.4).</item>
/// </list>
/// </summary>
public sealed class HarrisTaxonomySeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HapDbContext _db;
    private readonly string _definitionPath;

    public HarrisTaxonomySeeder(HapDbContext db, string definitionPath)
    {
        _db = db;
        _definitionPath = definitionPath;
    }

    public async Task<HarrisTaxonomySeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var definition = await LoadDefinitionAsync(_definitionPath, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var existingCategoryKeys = (await _db.HarrisCategories
                .Select(c => c.Key)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var categoriesCreated = 0;
        foreach (var categoryDef in definition.Categories)
        {
            if (existingCategoryKeys.Contains(categoryDef.Key))
            {
                continue;
            }

            _db.HarrisCategories.Add(HarrisCategory.Create(
                categoryDef.Key, categoryDef.Name, categoryDef.GroupReported, categoryDef.CustomerDeployed));
            categoriesCreated++;
        }

        var existingStages = (await _db.HarrisStageMaps
                .Select(s => s.InternalStage)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var stagesCreated = 0;
        foreach (var stageDef in definition.StageMap)
        {
            var internalStage = ParseEnum<InitiativeStage>(stageDef.InternalStage, "internal_stage");
            var harrisStage = ParseEnum<HarrisStage>(stageDef.HarrisStage, "harris_stage");
            if (existingStages.Contains(internalStage))
            {
                continue;
            }

            _db.HarrisStageMaps.Add(HarrisStageMap.Create(internalStage, harrisStage));
            stagesCreated++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new HarrisTaxonomySeedResult(
            categoriesCreated,
            definition.Categories.Count,
            stagesCreated,
            definition.StageMap.Count);
    }

    /// <summary>Exposed so tests can load the same definition the seeder would, and assert seeded rows
    /// match it by content — without ever hardcoding the taxonomy's strings (constitution Art. II.4).</summary>
    public static async Task<HarrisTaxonomyDefinition> LoadDefinitionAsync(
        string definitionPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(definitionPath))
        {
            throw new FileNotFoundException(
                $"Harris taxonomy definition not found at '{definitionPath}'. Expected the JSON shape " +
                "seeded from docs/frameworks/ (e.g. harris-taxonomy.v1.json).",
                definitionPath);
        }

        await using var stream = File.OpenRead(definitionPath);
        var definition = await JsonSerializer.DeserializeAsync<HarrisTaxonomyDefinition>(
            stream, JsonOptions, cancellationToken);

        return definition
            ?? throw new InvalidOperationException($"Harris taxonomy definition at '{definitionPath}' deserialised to null.");
    }

    private static TEnum ParseEnum<TEnum>(string raw, string field) where TEnum : struct =>
        Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Harris taxonomy '{field}' value '{raw}' is not a valid {typeof(TEnum).Name}.");
}
