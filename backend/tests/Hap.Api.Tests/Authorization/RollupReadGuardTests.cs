using System.Reflection;
using Hap.Api.Authorization;
using Xunit;

namespace Hap.Api.Tests.Authorization;

/// <summary>
/// The F2 guard (HAP-11, closing HAP-10's carry-forward): no aggregate read path can emit a number for a
/// SUPPRESSED node. Proven two ways, both structural rather than by-convention:
/// (1) <see cref="AggregateReadResult.Project"/> NEVER invokes the figures factory when the verdict is
/// suppressed — so the raw N/mean/distribution are not even read, let alone serialised; (2) the
/// <see cref="AggregateReadResult.Suppressed"/> shape carries no numeric member at all, so there is nowhere
/// for a figure to hide. Category=PrivacyReporting: this is the central privacy requirement of the story.
/// </summary>
[Trait("Category", "PrivacyReporting")]
public sealed class RollupReadGuardTests
{
    [Fact]
    public void Project_never_reads_the_figures_of_a_suppressed_node()
    {
        var factoryInvoked = false;

        var result = AggregateReadResult.Project(
            suppressed: true,
            reason: "N<4",
            figures: () =>
            {
                factoryInvoked = true; // reading the raw N/mean/distribution — must never happen when suppressed
                return new AggregateFigures(3, new Dictionary<string, double> { ["d"] = 2.5 },
                    new Dictionary<int, int> { [2] = 3 }, 1.0, 0.0);
            });

        Assert.False(factoryInvoked, "the figures factory (which reads the raw numbers) must not run for a suppressed node");
        var suppressed = Assert.IsType<AggregateReadResult.Suppressed>(result);
        Assert.Equal("N<4", suppressed.Reason);
        Assert.True(result.IsSuppressed);
    }

    [Fact]
    public void Project_publishes_figures_only_for_an_unsuppressed_node()
    {
        var figures = new AggregateFigures(4, new Dictionary<string, double> { ["d"] = 2.0 },
            new Dictionary<int, int> { [2] = 4 }, 1.0, 0.0);

        var result = AggregateReadResult.Project(suppressed: false, reason: null, figures: () => figures);

        var published = Assert.IsType<AggregateReadResult.Published>(result);
        Assert.Same(figures, published.Figures);
        Assert.False(result.IsSuppressed);
    }

    [Fact]
    public void A_missing_reason_defaults_to_the_below_threshold_marker_never_a_number()
    {
        var result = AggregateReadResult.Project(suppressed: true, reason: null, figures: () =>
            throw new InvalidOperationException("figures must not be read for a suppressed node"));

        var suppressed = Assert.IsType<AggregateReadResult.Suppressed>(result);
        Assert.Equal("N<4", suppressed.Reason);
    }

    [Fact]
    public void The_suppressed_shape_carries_no_numeric_member()
    {
        // Structural proof: a Suppressed result has exactly one readable member — the string Reason — so a
        // figure has nowhere to live. If a future edit adds a numeric field to Suppressed, this fails loudly.
        var numericTypes = new[] { typeof(int), typeof(long), typeof(double), typeof(decimal), typeof(float) };

        var members = typeof(AggregateReadResult.Suppressed)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(AggregateReadResult.Suppressed));

        foreach (var member in members)
        {
            Assert.DoesNotContain(member.PropertyType, numericTypes);
        }
    }
}
