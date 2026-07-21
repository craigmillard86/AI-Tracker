using System.Security.Claims;
using System.Text.Json;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api.Identity;

/// <summary>
/// The local dev <see cref="IIdentityProvider"/> (FR-055): no password accounts, no external IdP —
/// sign-in is picking one of the seeded users. <see cref="ChallengeAsync"/> answers
/// <c>GET /auth/signin</c> with the seed-user list (research D3: dev provider renders a role-picker);
/// <see cref="SignInAsync"/> answers <c>POST /auth/signin</c> by resolving the matching <c>Person</c>
/// (matched by <c>external_ref</c>, per contracts/api.md) and setting the ASP.NET Core auth cookie.
///
/// The principal carries <c>person_id</c> + explicit roles ONLY (see <see cref="IIdentityProvider"/>);
/// hierarchy roles are computed separately, per request, by <see cref="HierarchyRoleResolver"/>.
/// </summary>
public sealed class LocalDevProvider : IIdentityProvider
{
    /// <summary>Seed-user role labels that name an explicit <c>OrgRole</c> grant rather than a
    /// hierarchy tier. Driven entirely by the seed-users.json <c>role</c> field — never a hardcoded
    /// external_ref (Hap.Api does not reference Hap.Synth/Distributions). See QUESTIONS.md Q-013:
    /// this is a local-dev-provider-only bootstrap for the two fixtures that need an explicit grant
    /// before the role-grant admin endpoint (a later story) exists to create one.</summary>
    private static readonly IReadOnlyDictionary<string, OrgRole> ExplicitRoleBySeedLabel =
        new Dictionary<string, OrgRole>(StringComparer.Ordinal)
        {
            ["Platform Admin"] = OrgRole.PlatformAdmin,
            ["HIG Executive"] = OrgRole.HigExecutive,
        };

    private readonly HapDbContext _db;
    private readonly ISeedUserSource _seedUsers;
    private readonly IAuditWriter _audit;

    public LocalDevProvider(HapDbContext db, ISeedUserSource seedUsers, IAuditWriter audit)
    {
        _db = db;
        _seedUsers = seedUsers;
        _audit = audit;
    }

    public async Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var seedUsers = await _seedUsers.GetUsersAsync(cancellationToken);
        await context.Response.WriteAsJsonAsync(seedUsers, cancellationToken);
    }

    public async Task<ClaimsPrincipal> SignInAsync(
        HttpContext context, string userKey, CancellationToken cancellationToken = default)
    {
        var seedUsers = await _seedUsers.GetUsersAsync(cancellationToken);
        var seedUser = seedUsers.FirstOrDefault(u => string.Equals(u.ExternalRef, userKey, StringComparison.Ordinal))
            ?? throw new UnknownSeedUserException($"'{userKey}' is not a seeded dev user.");

        var person = await _db.People.FirstOrDefaultAsync(p => p.ExternalRef == userKey, cancellationToken)
            ?? throw new PersonNotSyncedException(
                $"'{userKey}' is a seeded user but no matching person exists yet — " +
                "has the directory been synced (POST /api/admin/sync)?");

        if (!person.IsActive)
        {
            throw new InactiveUserException($"'{userKey}' is deactivated and cannot sign in.");
        }

        await EnsureExplicitGrantAsync(person, seedUser.Role, cancellationToken);

        var grants = await _db.RoleGrants
            .Where(g => g.PersonId == person.Id)
            .ToListAsync(cancellationToken);

        // Exactly person_id + explicit roles (contracts/api.md; IIdentityProvider's own doc comment) —
        // no other claim. Person is matched by external_ref above, but external_ref itself is not
        // carried in the principal: nothing downstream consumes it, and the seam must stay
        // provider-agnostic (an OIDC principal would carry a subject id, not this port's lookup key).
        var claims = new List<Claim> { new("person_id", person.Id.ToString()) };
        claims.AddRange(grants.Select(g => new Claim(ClaimTypes.Role, g.Role.ToString())));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return principal;
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>Idempotently ensures the explicit RoleGrant a seed fixture's label implies exists,
    /// auditing the grant like any other (FR-050). A no-op for the five seed users whose label names
    /// a hierarchy tier, not an explicit role. See QUESTIONS.md Q-013.</summary>
    private async Task EnsureExplicitGrantAsync(Person person, string seedRoleLabel, CancellationToken cancellationToken)
    {
        if (!ExplicitRoleBySeedLabel.TryGetValue(seedRoleLabel, out var role))
        {
            return;
        }

        bool alreadyGranted = await _db.RoleGrants
            .AnyAsync(g => g.PersonId == person.Id && g.Role == role, cancellationToken);
        if (alreadyGranted)
        {
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var grant = RoleGrant.Create(person.Id, role, businessUnitId: null, grantedBy: "dev-seed");
        _db.RoleGrants.Add(grant);

        // Staged before SaveChangesAsync, same transaction — fail-closed like every other grant write.
        _audit.Record(AuditLog.Create(
            AuditAction.RoleGrant,
            actorPersonId: person.Id,
            subjectPersonId: person.Id,
            detail: JsonSerializer.Serialize(new
            {
                role = role.ToString(),
                grantedBy = "dev-seed",
                note = "local dev provider bootstrap (QUESTIONS.md Q-013)",
            })));

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
