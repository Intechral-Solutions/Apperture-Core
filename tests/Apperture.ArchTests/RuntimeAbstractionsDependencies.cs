using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

public sealed class RuntimeAbstractionsDependencies
{
    [Fact]
    public void Runtime_Abstractions_Should_Reference_Only_Kernel()
    {
        // Runtime.Abstractions should not reference Engine, Persistence, DTFx, or DurableFunctions
        var abstractionsAssembly = typeof(Apperture.Runtime.Abstractions.WorkflowDefinitionId).Assembly;
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOnAny("Apperture.Runtime.Engine", "Apperture.Runtime.Persistence", "Apperture.Runtime.DTFx", "Apperture.Runtime.DurableFunctions")
            .GetResult();

        Assert.True(result.IsSuccessful, result.FailingTypeNames?.Any() == true ? string.Join(", ", result.FailingTypeNames) : "Abstractions should not reference Engine or Persistence.");
    }
}
