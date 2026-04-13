using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// Enforces that the Windows worker host does not depend on Management HTTP/UI or operator-dashboard assemblies.
/// SQL-backed dispatch lives in Apperture.RuntimePlane.Dispatch.SqlServer; WorkerHost must not reference ControlPlane.Security.
/// </summary>
public sealed class RuntimeHostDependencies
{
    [Fact]
    public void WorkerHost_Should_Not_Depend_On_Management_Or_Operator_Ui()
    {
        var workerHostAssembly = typeof(Apperture.RuntimeHost.Windows.WorkerHost.StartRequestWorker).Assembly;
        var result = Types.InAssembly(workerHostAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Apperture.Management.Api",
                "Apperture.Management.Host",
                "Apperture.Management.Client",
                "Apperture.Management.Ui.Client",
                "Apperture.Management.Ui.Server",
                "Apperture.Management.Ui.Shared",
                "Apperture.ControlPlane.Dashboard",
                "Apperture.ControlPlane.Security")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"WorkerHost must not depend on Management, operator UI, or monolithic ControlPlane assemblies. Failing types: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.RuntimeHost.Windows.WorkerHost must not reference Management, Dashboard, or ControlPlane.Security.");
    }
}
