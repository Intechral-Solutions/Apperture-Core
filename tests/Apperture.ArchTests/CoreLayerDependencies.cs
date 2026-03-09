using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces dependency rules for the foundational Core layers (Shared Kernel, Core.Domain, Core.Application).
/// See docs/01-architecture/foundational-layers.md and .ai/playbooks/project-map.md.
/// </summary>
public sealed class CoreLayerDependencies
{
    [Fact]
    public void Shared_Kernel_Should_Not_Depend_On_Core_Domain_Or_Application()
    {
        var kernelAssembly = typeof(Apperture.Shared.Kernel.EntityId).Assembly;
        var result = Types.InAssembly(kernelAssembly)
            .Should()
            .NotHaveDependencyOnAny("Apperture.Core.Domain", "Apperture.Core.Application")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Kernel must not depend on Core. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Shared.Kernel must not reference Core.Domain or Core.Application.");
    }

    [Fact]
    public void Core_Domain_Should_Not_Depend_On_Core_Application()
    {
        var domainAssembly = typeof(Apperture.Core.Domain.Environment.EnvironmentId).Assembly;
        var result = Types.InAssembly(domainAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.Core.Application")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Core.Domain must not depend on Core.Application. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Core.Domain must depend only on Shared.Kernel (no Core.Application).");
    }

    [Fact]
    public void Core_Domain_Should_Not_Depend_On_RuntimePlane_Or_ControlPlane()
    {
        var domainAssembly = typeof(Apperture.Core.Domain.Environment.EnvironmentId).Assembly;
        var result = Types.InAssembly(domainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Apperture.RuntimePlane.Abstractions",
                "Apperture.RuntimePlane.Engine",
                "Apperture.RuntimePlane.Persistence",
                "Apperture.RuntimePlane.DTFx",
                "Apperture.RuntimePlane.DurableFunctions",
                "Apperture.Management.Api",
                "Apperture.Management.Host",
                "Apperture.Management.Client")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Core.Domain must not depend on Runtime or Control plane. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Core.Domain must depend only on Shared.Kernel.");
    }
}
