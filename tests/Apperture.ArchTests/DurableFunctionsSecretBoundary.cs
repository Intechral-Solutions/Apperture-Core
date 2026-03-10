using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces that workflow/orchestration assemblies do not expose ResolvedSecret or SecretMaterial,
/// so that only SecretRef-shaped values can cross Durable Functions orchestration boundaries.
/// See docs/01-architecture/durable-functions-secret-boundary.md.
/// </summary>
public sealed class DurableFunctionsSecretBoundary
{
    [Fact]
    public void RuntimePlane_Abstractions_Should_Not_Depend_On_Core_Security_Secrets()
    {
        var abstractionsAssembly = typeof(Apperture.RuntimePlane.Abstractions.StartWorkflowRequest).Assembly;
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Core.Security.Secrets")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"RuntimePlane.Abstractions must not depend on Core.Security.Secrets (orchestration must not reference ResolvedSecret/SecretMaterial). Failing: {string.Join(", ", result.FailingTypeNames)}"
                : "RuntimePlane.Abstractions must not reference Apperture.Core.Security.Secrets.");
    }

    [Fact]
    public void RuntimePlane_DurableFunctions_Should_Not_Depend_On_Core_Security_Secrets()
    {
        var durableAssembly = typeof(Apperture.RuntimePlane.DurableFunctions.DurableFunctionsRuntimeStub).Assembly;
        var result = Types.InAssembly(durableAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Core.Security.Secrets")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"RuntimePlane.DurableFunctions must not depend on Core.Security.Secrets (orchestration state must not serialize ResolvedSecret/SecretMaterial). Failing: {string.Join(", ", result.FailingTypeNames)}"
                : "RuntimePlane.DurableFunctions must not reference Apperture.Core.Security.Secrets.");
    }
}
