using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces dependency rules for the bootstrap subsystem.
/// See docs/01-architecture/bootstrap-and-secrets-boundary.md.
/// Bootstrap must not depend on Persistence or operational secret store (Core.Security.Secrets).
/// </summary>
public sealed class BootstrapFoundationDependencies
{
    [Fact]
    public void Bootstrap_Should_Not_Depend_On_Persistence()
    {
        var bootstrapAssembly = typeof(Apperture.Bootstrap.IBootstrapConnectionProfile).Assembly;
        var result = Types.InAssembly(bootstrapAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Persistence")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Bootstrap must not depend on Persistence. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Bootstrap must not reference Apperture.Persistence.");
    }

    [Fact]
    public void Bootstrap_Should_Not_Depend_On_Core_Security_Secrets()
    {
        var bootstrapAssembly = typeof(Apperture.Bootstrap.IBootstrapConnectionProfile).Assembly;
        var result = Types.InAssembly(bootstrapAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Core.Security.Secrets")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Bootstrap must not depend on operational secret store. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.Bootstrap must not reference Apperture.Core.Security.Secrets.");
    }
}
