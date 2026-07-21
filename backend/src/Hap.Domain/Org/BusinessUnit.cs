namespace Hap.Domain.Org;

/// <summary>One of ~23 business units (spec Key Entities). Registered with a group and,
/// through it, a portfolio. <see cref="IsOnboarded"/> drives cycle scope (FR-002); it is a
/// platform-admin decision, never set by directory sync.</summary>
public sealed class BusinessUnit
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public Guid GroupId { get; private set; }
    public bool IsOnboarded { get; private set; }
    public string? DirectorySource { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public BusinessUnit(
        Guid id,
        string code,
        string name,
        Guid groupId,
        bool isOnboarded,
        string? directorySource,
        DateTime createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        GroupId = groupId;
        IsOnboarded = isOnboarded;
        DirectorySource = directorySource;
        CreatedAt = createdAt;
    }

    public static BusinessUnit Create(string code, string name, Guid groupId, string? directorySource) =>
        new(Guid.NewGuid(), code, name, groupId, isOnboarded: false, directorySource, DateTime.UtcNow);

    /// <summary>Apply directory-sourced attributes on re-sync (name/group may move; code is the key).</summary>
    public void ApplyDirectory(string name, Guid groupId, string? directorySource)
    {
        Name = name;
        GroupId = groupId;
        DirectorySource = directorySource;
    }
}
