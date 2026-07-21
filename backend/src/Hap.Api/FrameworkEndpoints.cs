using Hap.Domain.Frameworks;
using Hap.Infrastructure;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;

namespace Hap.Api;

/// <summary>
/// Framework surfaces (contracts/api.md): <c>GET /api/frameworks/current</c> for any
/// authenticated caller (drives the assessment UI entirely from data — FR-001), and
/// <c>[PA] GET/POST /api/admin/frameworks</c> for platform-admin management.
///
/// AUTHORIZATION STATUS (HAP-4 rebase integration): <paramref name="api"/> is Program.cs's
/// authorized <c>/api</c> group (any authenticated session — HAP-4), so this whole surface is
/// no longer anonymous. <c>GET /frameworks/current</c> intentionally stays at that blanket
/// level (any authenticated role reads it — it drives the assessment UI). The
/// <c>/admin/frameworks</c> group additionally requires the <c>PlatformAdmin</c> policy, same
/// as <see cref="AdminEndpoints"/>'s admin group (see AdminEndpoints.cs's doc comment for the
/// red-team finding that closed this gap).
///
/// POST /api/admin/frameworks runs <see cref="FrameworkSeeder"/> against the configured
/// definition file, mirroring POST /api/admin/sync's role for the directory snapshot
/// (HAP-3) — an explicit, idempotent, re-runnable import rather than an implicit
/// startup step.
/// </summary>
public static class FrameworkEndpoints
{
    /// <summary><paramref name="api"/> is the already-authorized <c>/api</c> group
    /// (Program.cs), so both routes below are relative: <c>/api</c> + <c>/frameworks/current</c>
    /// and <c>/api</c> + <c>/admin/frameworks</c>, matching the pre-HAP-4 absolute paths exactly.</summary>
    public static void MapFrameworkEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/frameworks/current", async (HapDbContext db, CancellationToken ct) =>
        {
            var version = await db.FrameworkVersions
                .Where(v => v.Status == FrameworkVersionStatus.Active)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);

            if (version is null)
            {
                return Results.NotFound();
            }

            var framework = await db.Frameworks.SingleAsync(f => f.Id == version.FrameworkId, ct);

            var dimensions = await db.Dimensions
                .Where(d => d.FrameworkVersionId == version.Id)
                .OrderBy(d => d.DisplayOrder)
                .ToListAsync(ct);

            var dimensionIds = dimensions.Select(d => d.Id).ToList();
            var descriptors = await db.LevelDescriptors
                .Where(ld => dimensionIds.Contains(ld.DimensionId))
                .ToListAsync(ct);

            var response = new FrameworkCurrentResponse(
                framework.Key,
                framework.Name,
                version.VersionNumber,
                dimensions.Select(d => new DimensionResponse(
                    d.Key,
                    d.Name,
                    d.DisplayOrder,
                    descriptors
                        .Where(ld => ld.DimensionId == d.Id)
                        .OrderBy(ld => ld.Level)
                        .Select(ld => new LevelDescriptorResponse(ld.Level, ld.LevelName, ld.DescriptorText))
                        .ToList()))
                    .ToList());

            return Results.Ok(response);
        });

        var admin = api.MapGroup("/admin/frameworks").RequireAuthorization("PlatformAdmin");

        admin.MapGet("", async (FrameworkAdminService svc, CancellationToken ct) =>
        {
            var frameworks = await svc.ListAsync(ct);
            return Results.Ok(frameworks.Select(AdminFrameworkResponse.From));
        });

        admin.MapPost("", async (FrameworkSeeder seeder, CancellationToken ct) =>
        {
            var result = await seeder.SeedAsync(ct);
            return Results.Ok(result);
        });
    }
}

/// <summary>Wire shape of one 0–3 descriptor within a dimension.</summary>
public sealed record LevelDescriptorResponse(int Level, string LevelName, string DescriptorText);

/// <summary>Wire shape of one dimension, descriptors in level order.</summary>
public sealed record DimensionResponse(
    string Key, string Name, int DisplayOrder, IReadOnlyList<LevelDescriptorResponse> Levels);

/// <summary>Body of GET /api/frameworks/current — the active version, dimensions in display order.</summary>
public sealed record FrameworkCurrentResponse(
    string FrameworkKey, string FrameworkName, int VersionNumber, IReadOnlyList<DimensionResponse> Dimensions);

public sealed record AdminFrameworkVersionResponse(Guid Id, int VersionNumber, string Status, bool IsLocked);

public sealed record AdminFrameworkResponse(
    Guid Id, string Key, string Name, IReadOnlyList<AdminFrameworkVersionResponse> Versions)
{
    public static AdminFrameworkResponse From(FrameworkWithVersions fw) =>
        new(
            fw.Framework.Id,
            fw.Framework.Key,
            fw.Framework.Name,
            fw.Versions
                .Select(v => new AdminFrameworkVersionResponse(v.Id, v.VersionNumber, v.Status.ToString(), v.IsLocked))
                .ToList());
}
