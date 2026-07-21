using System.Security.Claims;

namespace Hap.Api.Identity;

/// <summary>
/// The identity port (contracts/api.md "Ports — IIdentityProvider"; constitution Art. IX.4 — one of
/// the only two ports; FR-055). The seam consumes and produces nothing provider-specific: a
/// <see cref="ClaimsPrincipal"/> carrying <c>person_id</c> + explicit roles is exactly what an OIDC
/// middleware would also produce (research D3), so the Entra ID adapter (a deferred,
/// decision-recorded story) implements this same interface without touching anything downstream.
///
/// Hierarchy-derived roles (Manager, BU Lead, Group Leader, Portfolio Leader) are deliberately NOT
/// part of the principal this port returns — they are computed per request from org data
/// (<see cref="HierarchyRoleResolver"/>), never stored, never cached in the session.
/// </summary>
public interface IIdentityProvider
{
    /// <summary>Initiates sign-in for an anonymous request. The dev provider writes the role-picker
    /// payload directly to the response; an OIDC adapter would instead call
    /// <c>context.ChallengeAsync(scheme)</c> to redirect to the identity provider.</summary>
    Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>Authenticates <paramref name="userKey"/>, establishes the session (the dev provider
    /// sets the auth cookie via <c>context.SignInAsync</c>), and returns the resulting principal.
    /// Throws a typed exception (see <c>IdentityExceptions.cs</c>) for every rejection reason so the
    /// endpoint can map each to the correct status code.</summary>
    Task<ClaimsPrincipal> SignInAsync(HttpContext context, string userKey, CancellationToken cancellationToken = default);

    /// <summary>Ends the session (the dev provider clears the auth cookie). Safe to call when no
    /// session exists.</summary>
    Task SignOutAsync(HttpContext context, CancellationToken cancellationToken = default);
}
