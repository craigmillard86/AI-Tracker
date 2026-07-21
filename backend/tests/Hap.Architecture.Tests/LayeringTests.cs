using System.Linq;
using Hap.Domain;
using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// Guards the dependency direction of the layers. The full rule set (via a
/// dedicated architecture-test library) lands with the domain model; the scaffold
/// asserts the load-bearing invariant: the domain layer depends on nothing internal.
/// </summary>
public class LayeringTests
{
    [Fact]
    public void Domain_does_not_reference_infrastructure_or_api()
    {
        var referenced = typeof(DomainMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.DoesNotContain("Hap.Infrastructure", referenced);
        Assert.DoesNotContain("Hap.Api", referenced);
    }
}
