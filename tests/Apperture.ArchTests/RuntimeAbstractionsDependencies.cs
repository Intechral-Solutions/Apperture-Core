using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

public sealed class RuntimeAbstractionsDependencies
{
    [Fact]
    public void Runtime_Abstractions_Should_Reference_Only_Kernel()
    {
        // RuntimePlane.Abstractions may reference Core.Domain (shared workflow vocabulary) but not Engine, Persistence, DTFx, or DurableFunctions.
        var abstractionsAssembly = typeof(Apperture.RuntimePlane.Abstractions.WorkflowDefinitionId).Assembly;
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOnAny("Apperture.RuntimePlane.Engine", "Apperture.RuntimePlane.Persistence", "Apperture.RuntimePlane.DTFx", "Apperture.RuntimePlane.DurableFunctions")
            .GetResult();

        Assert.True(result.IsSuccessful, result.FailingTypeNames?.Any() == true ? string.Join(", ", result.FailingTypeNames) : "Abstractions should not reference Engine or Persistence.");
    }
}
