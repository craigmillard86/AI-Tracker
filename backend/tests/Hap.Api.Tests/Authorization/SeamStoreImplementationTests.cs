using System.Linq;
using System.Reflection;
using Hap.Api.Authorization;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// The positive counterpart to <c>SeamBoundaryTests</c> (a source scan): a reflection assertion that
/// <see cref="SeamAssessmentStore"/> is the ONLY production type implementing either assessment-storage
/// port. The source scan proves no <c>Set&lt;&gt;()</c> query surface leaks outside the seam folder;
/// this proves no second implementation of the ports exists in production code to hold one — closing
/// the "another class could implement the port and query the tables" gap the regex alone can't (panel
/// advisory). Test doubles live in the TEST assembly and are intentionally out of scope. Category=
/// PrivacyReporting.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class SeamStoreImplementationTests
{
    // The production assembly that defines (and must solely implement) the ports.
    private static readonly Assembly ProductionAssembly = typeof(IAssessmentStore).Assembly;

    [Fact]
    public void SeamAssessmentStore_is_the_only_production_implementation_of_IAssessmentStore()
    {
        var implementers = ConcreteImplementersOf(typeof(IAssessmentStore));
        Assert.Equal(new[] { typeof(SeamAssessmentStore) }, implementers);
    }

    [Fact]
    public void SeamAssessmentStore_is_the_only_production_implementation_of_ISelfAssessmentStore()
    {
        var implementers = ConcreteImplementersOf(typeof(ISelfAssessmentStore));
        Assert.Equal(new[] { typeof(SeamAssessmentStore) }, implementers);
    }

    private static Type[] ConcreteImplementersOf(Type port) =>
        ProductionAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToArray();
}
