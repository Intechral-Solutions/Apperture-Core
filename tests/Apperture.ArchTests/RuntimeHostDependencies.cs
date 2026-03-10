using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces that runtime hosts do not depend on the Control Plane.
/// See docs/01-architecture/secrets-and-key-management.md and runtime-portability.
/// </summary>
public sealed class RuntimeHostDependencies
{
    [Fact]
    public void WorkerHost_Should_Not_Depend_On_ControlPlane()
    {
        var workerHostAssembly = typeof(Apperture.RuntimeHost.Windows.WorkerHost.WorkerStub).Assembly;
        var result = Types.InAssembly(workerHostAssembly)
            .Should()
            .NotHaveDependencyOn("Apperture.ControlPlane")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"WorkerHost must not depend on ControlPlane. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.RuntimeHost.Windows.WorkerHost must not reference Apperture.ControlPlane.");
    }
}
