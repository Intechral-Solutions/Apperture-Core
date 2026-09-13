using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// SDK-6A — integration modules must depend on the public SDK boundary, not platform persistence or control plane.
/// </summary>
public sealed class IntegrationSdkBoundaryDependencies
{
    // Intentionally explicit: architecture tests must stay stable and must not rely on brittle
    // build-output scanning or load-time heuristics. When adding a new integration module project
    // that should be covered by this protection, update this list.
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
        "Microsoft.Data.SqlClient",
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

    /// <summary>
    /// Guardrail for the intentional explicit list: every referenced integration module assembly
    /// under Apperture.Modules that exposes an IModule must appear in IntegrationModuleTypes.
    /// Automatic directory scanning was rejected as brittle for this protection.
    /// </summary>
    [Fact]
    public void Explicit_integration_module_list_covers_referenced_IModule_assemblies()
    {
        var listed = IntegrationModuleTypes
            .Select(t => t.Assembly.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

        var referencedModuleAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
            {
                var name = a.GetName().Name ?? "";
                return name.StartsWith("Sample.", StringComparison.Ordinal)
                    || name.StartsWith("Proof.", StringComparison.Ordinal)
                    || name.StartsWith("Unisa.", StringComparison.Ordinal);
            })
            .Where(a => a.GetTypes().Any(t =>
                typeof(Apperture.ModuleSdk.Abstractions.IModule).IsAssignableFrom(t)
                && t is { IsClass: true, IsAbstract: false }))
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = referencedModuleAssemblies.Except(listed).OrderBy(x => x).ToList();
        Assert.True(
            missing.Count == 0,
            "Add these integration modules to IntegrationSdkBoundaryDependencies.IntegrationModuleTypes: "
            + string.Join(", ", missing));
    }

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

    [Fact]
    public void Runtime_engine_adapters_may_reference_persistence_but_modules_may_not()
    {
        var engineAssembly = typeof(Apperture.RuntimePlane.Engine.Integration.IntegrationSettingsAccessor).Assembly;
        var engineRefs = engineAssembly.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Apperture.Persistence.SqlServer", engineRefs);

        foreach (var moduleType in IntegrationModuleTypes)
        {
            var moduleRefs = moduleType.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain("Apperture.Persistence.SqlServer", moduleRefs);
        }
    }
}
