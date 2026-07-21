using Hap.Domain.Org;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Hap.Api.Identity;

/// <summary>Registers cookie authentication, authorization, and the identity port + local dev
/// provider. Mirrors <c>InfrastructureServiceCollectionExtensions</c>'s pattern of keeping wiring
/// out of Program.cs.</summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddHapIdentity(this IServiceCollection services, string seedUsersPath)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "hap_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // This is an API, not a browser-navigated login page: an unauthenticated request to
                // an authorized endpoint must return 401/403, never a 302 redirect to a login page
                // that doesn't exist (the cookie handler's default behaviour).
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        // "PlatformAdmin" gates the admin endpoints (AdminEndpoints.cs) — closed in this story after
        // hap-red-team proved a concrete self-escalation path through the previously auth-only
        // (not role-gated) override endpoint. RequireRole checks the Role claims LocalDevProvider
        // populates from RoleGrant rows, so this composes with the outer "any authenticated session"
        // policy already on the /api group: unauthenticated -> 401 (Challenge), authenticated but
        // not PlatformAdmin -> 403 (Forbid, per OnRedirectToAccessDenied above).
        services.AddAuthorization(options =>
        {
            options.AddPolicy("PlatformAdmin", policy => policy.RequireRole(nameof(OrgRole.PlatformAdmin)));
        });

        services.AddScoped<ISeedUserSource>(_ => new FileSeedUserSource(seedUsersPath));
        services.AddScoped<IIdentityProvider, LocalDevProvider>();
        services.AddScoped<HierarchyRoleResolver>();

        return services;
    }
}
