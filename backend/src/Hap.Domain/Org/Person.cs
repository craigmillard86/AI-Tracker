namespace Hap.Domain.Org;

/// <summary>
/// A person synced from the directory (spec Key Entities; FR-020/021/024). Keyed to the
/// directory by <see cref="ExternalRef"/>. Mutation is confined to intent-named methods so
/// there is exactly one write surface — directory sync (and the override re-apply that runs
/// inside it). There is no direct person CRUD anywhere: leavers are deactivated
/// (<see cref="Deactivate"/>), never deleted (FR-024).
/// Team is derived (a manager and their active direct reports) — there is no Team entity.
/// </summary>
public sealed class Person
{
    public Guid Id { get; private set; }
    public string ExternalRef { get; private set; }
    public string DisplayName { get; private set; }
    public string Email { get; private set; }
    public string JobTitle { get; private set; }
    public Guid? ManagerPersonId { get; private set; }
    public Guid BusinessUnitId { get; private set; }
    public EmployeeType EmployeeType { get; private set; }
    public bool IsActive { get; private set; }
    public bool OnLeave { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Person(
        Guid id,
        string externalRef,
        string displayName,
        string email,
        string jobTitle,
        Guid? managerPersonId,
        Guid businessUnitId,
        EmployeeType employeeType,
        bool isActive,
        bool onLeave,
        DateTime createdAt)
    {
        Id = id;
        ExternalRef = externalRef;
        DisplayName = displayName;
        Email = email;
        JobTitle = jobTitle;
        ManagerPersonId = managerPersonId;
        BusinessUnitId = businessUnitId;
        EmployeeType = employeeType;
        IsActive = isActive;
        OnLeave = onLeave;
        CreatedAt = createdAt;
    }

    public static Person Create(
        string externalRef,
        string displayName,
        string email,
        string jobTitle,
        Guid businessUnitId,
        EmployeeType employeeType,
        bool isActive,
        bool onLeave) =>
        new(Guid.NewGuid(), externalRef, displayName, email, jobTitle,
            managerPersonId: null, businessUnitId, employeeType, isActive, onLeave, DateTime.UtcNow);

    /// <summary>Apply the mutable directory attributes carried by a snapshot row (idempotent —
    /// re-applying the same snapshot is a no-op at the value level). Manager and BU are set
    /// separately because they resolve through the external-ref/code maps.</summary>
    public void ApplyDirectoryAttributes(
        string displayName,
        string email,
        string jobTitle,
        EmployeeType employeeType,
        bool isActive,
        bool onLeave)
    {
        DisplayName = displayName;
        Email = email;
        JobTitle = jobTitle;
        EmployeeType = employeeType;
        IsActive = isActive;
        OnLeave = onLeave;
    }

    public void SetBusinessUnit(Guid businessUnitId) => BusinessUnitId = businessUnitId;

    public void SetManager(Guid? managerPersonId) => ManagerPersonId = managerPersonId;

    /// <summary>Leaver handling (FR-024): flag inactive, retain the row and its history.</summary>
    public void Deactivate() => IsActive = false;
}
