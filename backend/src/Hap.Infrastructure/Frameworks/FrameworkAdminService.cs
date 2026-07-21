using Hap.Domain.Frameworks;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Frameworks;

/// <summary>A framework with its versions, newest last, for admin listing.</summary>
public sealed record FrameworkWithVersions(Framework Framework, IReadOnlyList<FrameworkVersion> Versions);

/// <summary>
/// Read/management surface for `[PA] GET/POST /api/admin/frameworks` beyond seeding (FR-054).
/// <see cref="CreateDraftVersionAsync"/> exists to exercise and prove the versioning invariant
/// ("creating v2 leaves v1 untouched; current still returns the active version") — it is not
/// yet wired to an HTTP route in HAP-6. Populating a draft version's content, and an admin
/// activation endpoint, are a future admin-workflow story (constitution Art. IX.4, YAGNI: no
/// speculative surface ahead of a consumer); QUESTIONS.md Q-004 sets the precedent of shipping
/// admin capability API/service-first while UI/full workflow catches up.
/// </summary>
public sealed class FrameworkAdminService
{
    private readonly HapDbContext _db;

    public FrameworkAdminService(HapDbContext db) => _db = db;

    public async Task<IReadOnlyList<FrameworkWithVersions>> ListAsync(CancellationToken cancellationToken = default)
    {
        var frameworks = await _db.Frameworks.ToListAsync(cancellationToken);
        var versions = await _db.FrameworkVersions.ToListAsync(cancellationToken);

        return frameworks
            .Select(f => new FrameworkWithVersions(
                f,
                versions.Where(v => v.FrameworkId == f.Id).OrderBy(v => v.VersionNumber).ToList()))
            .ToList();
    }

    /// <summary>Creates the next draft version for a framework: unlocked, empty of
    /// dimensions/descriptors, never auto-activated. Existing versions and any assessment data
    /// scored against them are untouched (FR-054).</summary>
    public async Task<FrameworkVersion> CreateDraftVersionAsync(
        string frameworkKey, string? sourceRef, CancellationToken cancellationToken = default)
    {
        var framework = await _db.Frameworks.SingleOrDefaultAsync(f => f.Key == frameworkKey, cancellationToken)
            ?? throw new InvalidOperationException($"Unknown framework key '{frameworkKey}'.");

        var maxExisting = await _db.FrameworkVersions
            .Where(v => v.FrameworkId == framework.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken);
        var nextVersionNumber = (maxExisting ?? 0) + 1;

        var version = FrameworkVersion.Create(framework.Id, nextVersionNumber, sourceRef);
        _db.FrameworkVersions.Add(version);
        await _db.SaveChangesAsync(cancellationToken);
        return version;
    }
}
