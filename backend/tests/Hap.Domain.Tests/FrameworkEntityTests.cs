using System.Linq;
using System.Reflection;
using Hap.Domain.Frameworks;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>
/// FR-054 domain guard: once a <see cref="FrameworkVersion"/> is locked (a Cycle has adopted
/// it — HAP-7 will call <see cref="FrameworkVersion.Lock"/> at cycle open), writes to the
/// version itself must be rejected. This is the "domain guard test" HAP-6's acceptance criteria
/// require, exercised directly against the entity with no database involved.
/// </summary>
public class FrameworkEntityTests
{
    [Fact]
    public void EnsureMutable_passes_on_a_fresh_unlocked_version()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");

        var ex = Record.Exception(() => version.EnsureMutable());

        Assert.Null(ex);
    }

    [Fact]
    public void Lock_causes_EnsureMutable_to_reject_further_writes()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");

        version.Lock();

        var ex = Assert.Throws<FrameworkVersionLockedException>(() => version.EnsureMutable());
        Assert.Equal(version.Id, ex.FrameworkVersionId);
    }

    [Fact]
    public void Lock_is_idempotent()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");

        version.Lock();
        var ex = Record.Exception(() => version.Lock());

        Assert.Null(ex);
        Assert.True(version.IsLocked);
    }

    [Fact]
    public void Activate_once_locked_is_rejected_like_any_other_write()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Lock();

        Assert.Throws<FrameworkVersionLockedException>(() => version.Activate());
    }

    [Fact]
    public void Retire_once_locked_is_rejected_like_any_other_write()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Lock();

        Assert.Throws<FrameworkVersionLockedException>(() => version.Retire());
    }

    [Fact]
    public void Activate_is_idempotent_while_unlocked()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");

        version.Activate();
        version.Activate();

        Assert.Equal(FrameworkVersionStatus.Active, version.Status);
    }

    [Fact]
    public void Retired_version_cannot_be_reactivated()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Activate();
        version.Retire();

        Assert.Throws<InvalidOperationException>(() => version.Activate());
    }

    [Fact]
    public void AddDimension_is_rejected_once_the_version_is_locked()
    {
        // Panel round-1 B2: this is the domain-level proof that content creation itself is
        // guarded, not just the version's own state transitions.
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        version.Lock();

        var ex = Assert.Throws<FrameworkVersionLockedException>(() => version.AddDimension("k", "n", 0));
        Assert.Equal(version.Id, ex.FrameworkVersionId);
    }

    [Fact]
    public void AddLevelDescriptor_is_rejected_once_the_version_is_locked()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        var dimension = version.AddDimension("k", "n", 0); // built while still unlocked
        version.Lock();

        var ex = Assert.Throws<FrameworkVersionLockedException>(
            () => version.AddLevelDescriptor(dimension, 0, "L0", "text"));
        Assert.Equal(version.Id, ex.FrameworkVersionId);
    }

    [Fact]
    public void AddLevelDescriptor_rejects_a_dimension_from_a_different_version()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");
        var otherVersion = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/y.json");
        var foreignDimension = otherVersion.AddDimension("k", "n", 0);

        Assert.Throws<InvalidOperationException>(() => version.AddLevelDescriptor(foreignDimension, 0, "L0", "text"));
    }

    [Fact]
    public void New_version_starts_Draft_and_unlocked()
    {
        var version = FrameworkVersion.Create(Guid.NewGuid(), 1, "docs/frameworks/x.json");

        Assert.Equal(FrameworkVersionStatus.Draft, version.Status);
        Assert.False(version.IsLocked);
    }

    [Fact]
    public void Framework_and_content_entities_expose_no_property_setters()
    {
        foreach (var type in new[] { typeof(Framework), typeof(Dimension), typeof(LevelDescriptor) })
        {
            var mutable = type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.SetMethod is not null)
                .Select(p => p.Name)
                .ToList();

            Assert.True(mutable.Count == 0,
                $"{type.Name} must be immutable once created: no setters permitted, found: {string.Join(", ", mutable)}");
        }
    }
}
