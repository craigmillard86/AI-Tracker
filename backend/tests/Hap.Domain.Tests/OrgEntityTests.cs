using System.Linq;
using System.Reflection;
using Hap.Domain.Audit;
using Hap.Domain.Org;
using Xunit;

namespace Hap.Domain.Tests;

/// <summary>Unit coverage for the HAP-3 org/audit domain entities: the mutation surface of
/// <see cref="Person"/> is confined to intent-named methods, and the append-only records
/// (<see cref="AuditLog"/>, <see cref="OrgOverride"/>, <see cref="RoleGrant"/>) expose no way
/// to mutate state once created.</summary>
public class OrgEntityTests
{
    [Fact]
    public void Person_deactivate_flags_inactive_without_dropping_identity()
    {
        var person = Person.Create("HAP-X-1", "Ada L", "ada@synth.local", "Engineer",
            businessUnitId: Guid.NewGuid(), EmployeeType.Employee, isActive: true, onLeave: false);

        person.Deactivate();

        Assert.False(person.IsActive);
        Assert.Equal("HAP-X-1", person.ExternalRef); // row retained, not deleted (FR-024)
    }

    [Fact]
    public void Person_directory_attributes_are_overwritten_on_reapply()
    {
        var person = Person.Create("HAP-X-2", "Old Name", "old@synth.local", "Analyst",
            Guid.NewGuid(), EmployeeType.Employee, isActive: true, onLeave: false);

        person.ApplyDirectoryAttributes("New Name", "new@synth.local", "Senior Analyst",
            EmployeeType.Contractor, isActive: true, onLeave: true);

        Assert.Equal("New Name", person.DisplayName);
        Assert.Equal("new@synth.local", person.Email);
        Assert.Equal("Senior Analyst", person.JobTitle);
        Assert.Equal(EmployeeType.Contractor, person.EmployeeType);
        Assert.True(person.OnLeave);
    }

    [Fact]
    public void AuditLog_exposes_no_property_setters_at_all()
    {
        var mutable = typeof(AuditLog)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.True(mutable.Count == 0,
            $"AuditLog must be append-only: no setters permitted, found: {string.Join(", ", mutable)}");
    }

    [Fact]
    public void AuditLog_create_defaults_blank_detail_to_empty_object()
    {
        var entry = AuditLog.Create(AuditAction.OrgOverride, actorPersonId: null, subjectPersonId: Guid.NewGuid(), detail: "  ");
        Assert.Equal("{}", entry.Detail);
    }

    [Fact]
    public void OrgOverride_and_RoleGrant_expose_no_property_setters()
    {
        foreach (var type in new[] { typeof(OrgOverride), typeof(RoleGrant) })
        {
            var mutable = type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.SetMethod is not null)
                .Select(p => p.Name)
                .ToList();

            Assert.True(mutable.Count == 0,
                $"{type.Name} must be immutable, found setters: {string.Join(", ", mutable)}");
        }
    }
}
