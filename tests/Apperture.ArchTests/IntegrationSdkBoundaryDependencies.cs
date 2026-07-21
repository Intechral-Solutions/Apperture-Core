using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// SDK-6A — integration modules must depend on the public SDK boundary, not platform persistence or control plane.
/// </summary>
public sealed class IntegrationSdkBoundaryDependencies
{
    private static readonly Type[] IntegrationModuleTypes =
    [
        typeof(Sample.HelloWorld.HelloWorldModule),
        typeof(Sample.FileImport.FileImportModule),
        typeof(Sample.ApiPolling.ApiPollingModule),
        typeof(Proof.CampusNexus.StudentReview.ProofCampusNexusStudentReviewModule),
        typeof(Unisa.CampusNexus.PaymentPlans.UnisaCampusNexusPaymentPlansModule),
    ];

    private static readonly string[] ProhibitedModuleDependencies =
    [
        "Apperture.Persistence.SqlServer",
        "Apperture.Management.Api",
        "Apperture.Management.Host",
        "Apperture.Management.Client",
        "Apperture.RuntimeHost.Windows.WorkerHost",
        "Microsoft.EntityFrameworkCore",
    ];

    [Theory]
    [MemberData(nameof(GetIntegrationModuleTypes))]
    public void Integration_Module_Assemblies_Must_Not_Reference_Platform_Internals(Type moduleType)
    {
        var result = Types.InAssembly(moduleType.Assembly)
            .Should()
            .NotHaveDependencyOnAny(ProhibitedModuleDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"{moduleType.Assembly.GetName().Name} must not reference platform internals. Failing: {string.Join(", ", result.FailingTypeNames)}"
                : $"{moduleType.Assembly.GetName().Name} references prohibited platform assemblies.");
    }

    public static IEnumerable<object[]> GetIntegrationModuleTypes() =>
        IntegrationModuleTypes.Select(x => new object[] { x });

    [Fact]
    public void ModuleSdk_Integration_Must_Not_Reference_EF_Core_Or_Persistence()
    {
        var integrationAssembly = typeof(Apperture.Core.Application.Execution.IStepHandler).Assembly;
        var result = Types.InAssembly(integrationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Apperture.Persistence.SqlServer",
                "Apperture.Management.Api",
                "Apperture.Management.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, "Apperture.ModuleSdk.Integration must remain persistence-free.");
    }

    [Fact]
    public void ModuleSdk_Integration_Must_Not_Reference_Core_Application_Assembly()
    {
        var integrationAssembly = typeof(Apperture.Core.Application.Execution.IStepHandler).Assembly;
        var referenced = integrationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Apperture.Core.Application", referenced);
        Assert.DoesNotContain("Apperture.Persistence.SqlServer", referenced);
    }

    [Fact]
    public void Integration_Testing_Toolkit_Must_Not_Reference_Platform_Internals()
    {
        var assembly = typeof(Apperture.ModuleSdk.Integration.Testing.IntegrationProcedureTestHost).Assembly;
        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Apperture.Persistence.SqlServer", referenced);
        Assert.DoesNotContain("Apperture.Management.Api", referenced);
        Assert.DoesNotContain("Apperture.Management.Host", referenced);
        Assert.DoesNotContain("Apperture.RuntimePlane.Engine", referenced);
    }
}
