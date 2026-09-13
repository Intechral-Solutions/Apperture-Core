using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>
/// SDK-6D — operator UI and integration SDK boundaries: UI layers stay API-client-only;
/// modules stay off Management UI; ModuleSdk.Integration stays free of Blazor/ASP.NET UI packages.
/// </summary>
public sealed class Sdk6dOperatorUiBoundaryDependencies
{
    private static readonly string[] UiProhibitedDependencies =
    [
        "Apperture.Persistence.SqlServer",
        "Apperture.Persistence",
        "Apperture.Core.Security.Secrets",
        "Microsoft.EntityFrameworkCore",
    ];

    private static readonly string[] ModuleUiProhibitedDependencies =
    [
        "Apperture.Management.Ui.Client",
        "Apperture.Management.Ui.Shared",
        "Apperture.Management.Ui.Server",
    ];

    private static readonly string[] ModuleSdkIntegrationUiProhibitedDependencies =
    [
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Web",
        "Microsoft.AspNetCore.Components.WebAssembly",
        "Microsoft.AspNetCore.Components.Authorization",
        "Microsoft.AspNetCore.App",
    ];

    [Fact]
    public void Management_Ui_Shared_Must_Not_Reference_Persistence_Or_Secret_Store()
    {
        var assembly = typeof(Apperture.Management.Ui.Shared.IntegrationConfigurationOperatorUiHelpers).Assembly;
        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(UiProhibitedDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Management.Ui.Shared must not reference persistence or secret store. Failing: {string.Join(", ", result.FailingTypeNames)}"
                : "Management.Ui.Shared references prohibited persistence or secret assemblies.");
    }

    [Fact]
    public void Management_Ui_Client_Must_Not_Reference_Persistence_Or_Secret_Store()
    {
        var assembly = typeof(Apperture.Management.Ui.Client.Services.DomainMenuService).Assembly;
        var result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(UiProhibitedDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"Management.Ui.Client must not reference persistence or secret store. Failing: {string.Join(", ", result.FailingTypeNames)}"
                : "Management.Ui.Client references prohibited persistence or secret assemblies.");
    }

    [Theory]
    [MemberData(nameof(GetIntegrationModuleTypes))]
    public void Integration_Module_Assemblies_Must_Not_Reference_Management_Ui(Type moduleType)
    {
        var result = Types.InAssembly(moduleType.Assembly)
            .Should()
            .NotHaveDependencyOnAny(ModuleUiProhibitedDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"{moduleType.Assembly.GetName().Name} must not reference Management UI assemblies. Failing: {string.Join(", ", result.FailingTypeNames)}"
                : $"{moduleType.Assembly.GetName().Name} references Management UI assemblies.");
    }

    [Fact]
    public void ModuleSdk_Integration_Must_Not_Reference_Blazor_Or_AspNet_Ui_Packages()
    {
        var integrationAssembly = typeof(Apperture.ModuleSdk.Integration.Readiness.ConfigurationReadinessEvaluator).Assembly;
        var result = Types.InAssembly(integrationAssembly)
            .Should()
            .NotHaveDependencyOnAny(ModuleSdkIntegrationUiProhibitedDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            result.FailingTypeNames?.Any() == true
                ? $"ModuleSdk.Integration must not reference Blazor/ASP.NET UI packages. Failing: {string.Join(", ", result.FailingTypeNames)}"
                : "Apperture.ModuleSdk.Integration references Blazor or ASP.NET UI packages.");
    }

    [Fact]
    public void Management_Ui_Shared_Must_Not_Duplicate_Platform_Readiness_Evaluators()
    {
        var assembly = typeof(Apperture.Management.Ui.Shared.IntegrationConfigurationOperatorUiHelpers).Assembly;
        var uiTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsPublic: true }).ToList();

        var duplicateEvaluatorTypes = uiTypes
            .Where(t => t.Name.Contains("ReadinessEvaluator", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            duplicateEvaluatorTypes.Count == 0,
            "Management.Ui.Shared must not define readiness evaluators; use API readiness DTOs only. Found: "
            + string.Join(", ", duplicateEvaluatorTypes));

        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Apperture.Core.Application", referenced);
        Assert.DoesNotContain(typeof(Apperture.ModuleSdk.Integration.Readiness.ConfigurationReadinessEvaluator).Assembly.GetName().Name, referenced);
    }

    public static IEnumerable<object[]> GetIntegrationModuleTypes() =>
        IntegrationSdkBoundaryDependencies.GetIntegrationModuleTypes();
}
