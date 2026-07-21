namespace Hap.Api.Authorization;

/// <summary>
/// Registers the visibility seam (CLAUDE.md §2: <c>Hap.Api/Authorization</c> is THE visibility seam).
/// Mirrors the identity/infrastructure wiring extensions, keeping composition out of Program.cs.
///
/// <para>Registered now: the seam foundation consumed by the org side and by
/// <see cref="OrgGraphLoader"/>. NOT registered: <see cref="AssessmentReads"/> and its
/// <see cref="IAssessmentStore"/> — those come online with HAP-8, which adds the DbSet-backed store; the
/// gateway is exercised now via unit tests with a fake store, so no half-wired production graph is left
/// dangling.</para>
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddHapAuthorization(this IServiceCollection services)
    {
        services.AddSingleton(SeamOptions.Default);
        services.AddSingleton<ChainResolver>();
        services.AddSingleton<SuppressionEvaluator>();
        services.AddScoped<OrgGraphLoader>();
        return services;
    }
}
