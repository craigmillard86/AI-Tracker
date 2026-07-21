using Hap.Infrastructure.Audit;
using Hap.Infrastructure.Directory;
using Microsoft.Extensions.DependencyInjection;

namespace Hap.Infrastructure;

/// <summary>Registers the HAP infrastructure services (directory port + importer, audit writer,
/// override service). Kept out of <c>Program.cs</c> so the wiring is testable and reusable.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHapInfrastructure(this IServiceCollection services, string directorySnapshotPath)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IDirectorySource>(_ => new SyntheticDirectoryAdapter(directorySnapshotPath));
        services.AddScoped<DirectoryImportService>();
        services.AddScoped<OrgOverrideService>();
        return services;
    }
}
