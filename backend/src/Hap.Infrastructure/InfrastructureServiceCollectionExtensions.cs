using Hap.Infrastructure.Audit;
using Hap.Infrastructure.Cycles;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Email;
using Hap.Infrastructure.Frameworks;
using Hap.Infrastructure.Notifications;
using Hap.Infrastructure.Register;
using Microsoft.Extensions.DependencyInjection;

namespace Hap.Infrastructure;

/// <summary>Registers the HAP infrastructure services (directory port + importer, audit writer,
/// override service, framework seeder + admin service, cycle service, email + notification job
/// service). Kept out of <c>Program.cs</c> so the wiring is testable and reusable.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddHapInfrastructure(
        this IServiceCollection services,
        string directorySnapshotPath,
        string frameworkDefinitionPath,
        string harrisTaxonomyDefinitionPath,
        string mailpitApiBaseUrl)
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

        // Email + notifications (HAP-18, FR-037/FR-057/FR-061). IEmailSender/ISentMailLedger point at
        // mailpit ONLY — no other SMTP/API destination exists anywhere in this codebase (see
        // SmtpOptions/MailpitSentMailLedger class docs). SmtpOptions itself is bound via
        // IOptions<SmtpOptions> in Program.cs (standard ASP.NET convention), not here.
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<EmailTemplateRenderer>();
        services.AddHttpClient<MailpitSentMailLedger>(client =>
        {
            client.BaseAddress = new Uri(mailpitApiBaseUrl);
        });
        services.AddScoped<ISentMailLedger>(sp => sp.GetRequiredService<MailpitSentMailLedger>());
        services.AddScoped<NotificationJobService>();

        return services;
    }
}
