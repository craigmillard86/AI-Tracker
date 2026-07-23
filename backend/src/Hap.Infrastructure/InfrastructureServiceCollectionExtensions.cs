using Hap.Infrastructure.Audit;
using Hap.Infrastructure.Cycles;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Hap.Infrastructure.Register;
using Microsoft.Extensions.DependencyInjection;

namespace Hap.Infrastructure;

/// <summary>Registers the HAP infrastructure services (directory port + importer, audit writer,
/// override service, framework seeder + admin service, cycle service). Kept out of
/// <c>Program.cs</c> so the wiring is testable and reusable.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHapInfrastructure(
        this IServiceCollection services,
        string directorySnapshotPath,
        string frameworkDefinitionPath,
        string harrisTaxonomyDefinitionPath)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IDirectorySource>(_ => new SyntheticDirectoryAdapter(directorySnapshotPath));
        services.AddScoped<DirectoryImportService>();
        services.AddScoped<OrgOverrideService>();
        services.AddScoped(sp => new FrameworkSeeder(sp.GetRequiredService<HapDbContext>(), frameworkDefinitionPath));
        services.AddScoped<FrameworkAdminService>();
        services.AddScoped<CycleService>();
        services.AddScoped(sp =>
            new HarrisTaxonomySeeder(sp.GetRequiredService<HapDbContext>(), harrisTaxonomyDefinitionPath));
        return services;
    }
}
