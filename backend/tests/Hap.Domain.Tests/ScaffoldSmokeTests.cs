using Hap.Domain;
using Xunit;

namespace Hap.Domain.Tests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void Domain_assembly_is_referencable()
    {
        Assert.Equal("Hap.Domain", typeof(DomainMarker).Assembly.GetName().Name);
    }
}
