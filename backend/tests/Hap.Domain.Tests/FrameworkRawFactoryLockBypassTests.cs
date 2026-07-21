using Hap.Domain.Frameworks;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-6 (fresh-instance QA pass, CLAUDE.md §9). The L2 panel
/// (round 1 B2 fix, round 2 sign-off) wired FR-054's EnsureMutable() into
/// FrameworkVersion.AddDimension/AddLevelDescriptor and documented those as "the sole path" for
/// creating content — but both panel rounds explicitly carried forward, unfixed, that
/// Dimension.Create and LevelDescriptor.Create remain public static factories with no reference
/// back to the owning FrameworkVersion. This is the mandatory §9.3(a)-style probe of that exact
/// gap: does the raw factory actually let content be constructed for a version that is locked?
/// It does — proven directly against the entities, no database involved. See
/// Hap.Api.Tests.FrameworkLockBypassQaTests for the same finding proven end to end through a
/// persisted write.
/// </summary>
public class FrameworkRawFactoryLockBypassTests
{
    [Fact]
    public void Dimension_Create_the_raw_factory_ignores_a_locked_version_and_succeeds()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Lock();

        // Dimension.Create takes only the version's Id (a Guid) — never a reference to the
        // FrameworkVersion object itself — so it has no way to consult IsLocked even in
        // principle. This is a real, reachable bypass of the "sole path" documented on
        // FrameworkVersion.AddDimension, not a hypothetical one.
        var ex = Record.Exception(() => Dimension.Create(version.Id, "attacker-key", "Attacker Dimension", 99));

        Assert.Null(ex); // no guard fires — the gap
    }

    [Fact]
    public void LevelDescriptor_Create_the_raw_factory_ignores_a_locked_version_and_succeeds()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        var dimension = version.AddDimension("k", "n", 0); // built while still unlocked, as the seeder would
        version.Lock();

        var ex = Record.Exception(() =>
            LevelDescriptor.Create(dimension.Id, 0, "attacker-level", "attacker descriptor text"));

        Assert.Null(ex); // no guard fires here either
    }

    [Fact]
    public void Contrast_the_guarded_AddDimension_path_correctly_rejects_the_same_write_the_raw_factory_allows()
    {
        // Same content, same locked version, added through the domain-guarded "sole path" —
        // correctly rejected. This proves the finding above is specifically a raw-factory gap,
        // not a general failure of FR-054's write-path guard.
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Lock();

        Assert.Throws<FrameworkVersionLockedException>(() => version.AddDimension("attacker-key", "Attacker Dimension", 99));
    }
}
