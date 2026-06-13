using CsCheck;
using Xunit;

namespace Orderly.Tests.Support;

/// <summary>
/// Smoke tests for the property-based testing harness itself. They confirm the CsCheck
/// runner is wired into xUnit and that the shared <see cref="PbtConfig.MinIterations"/>
/// minimum is actually enforced on a real property sample.
/// </summary>
public class PbtHarnessTests
{
    [Fact]
    public void Global_iteration_budget_is_at_least_the_configured_minimum()
    {
        Assert.True(
            Check.Iter >= PbtConfig.MinIterations,
            $"Expected the global CsCheck iteration budget to be at least " +
            $"{PbtConfig.MinIterations} but it was {Check.Iter}.");
    }

    [Fact]
    public void Property_sample_runs_at_least_the_configured_minimum_iterations()
    {
        var count = 0;

        Gen.Int.Sample(_ => Interlocked.Increment(ref count), iter: PbtConfig.MinIterations);

        Assert.True(
            count >= PbtConfig.MinIterations,
            $"Expected at least {PbtConfig.MinIterations} iterations but observed {count}.");
    }
}
