namespace Hap.Api.Authorization;

/// <summary>
/// Registers the visibility seam (CLAUDE.md §2: <c>Hap.Api/Authorization</c> is THE visibility seam).
/// Mirrors the identity/infrastructure wiring extensions, keeping composition out of Program.cs.
///
/// <para>Registered now: the seam foundation (org side + <see cref="OrgGraphLoader"/>) AND — from
/// HAP-8 — the assessment-read gateway (<see cref="AssessmentReads"/>) over the real DbSet-backed
/// store (<see cref="SeamAssessmentStore"/>), plus the self-scope workflow
/// (<see cref="SelfAssessmentService"/>). These are scoped: they wrap the request-scoped
/// <c>HapDbContext</c>. The store is the ONLY registered <see cref="IAssessmentStore"/>, so every
/// production assessment query funnels through the seam.</para>
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddHapAuthorization(this IServiceCollection services)
    {
        services.AddSingleton(SeamOptions.Default);
        services.AddSingleton<ChainResolver>();
        services.AddSingleton<SuppressionEvaluator>();
        services.AddScoped<OrgGraphLoader>();

        // The assessment seam (HAP-8): one DbSet-backed store behind both storage ports (cross-person
        // read + self-scope), the read gateway, and the self-scope workflow. Scoped — each wraps the
        // request-scoped HapDbContext; the two port registrations forward to the single store instance.
        services.AddScoped<SeamAssessmentStore>();
        services.AddScoped<IAssessmentStore>(sp => sp.GetRequiredService<SeamAssessmentStore>());
        services.AddScoped<ISelfAssessmentStore>(sp => sp.GetRequiredService<SeamAssessmentStore>());
        services.AddScoped<AssessmentReads>();
        services.AddScoped<SelfAssessmentService>();
        return services;
    }
}
