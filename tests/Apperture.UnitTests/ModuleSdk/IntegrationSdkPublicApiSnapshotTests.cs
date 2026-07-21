using System.Reflection;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

/// <summary>
/// Lightweight public API regression guard for SDK-6A integration contracts.
/// </summary>
public sealed class IntegrationSdkPublicApiSnapshotTests
{
  private static readonly string[] ExpectedPublicTypes =
  [
      // Execution
      nameof(IStepHandler),
      nameof(IStepExecutionContext),
      nameof(IStepExecutionSignals),
      nameof(IModuleStepHandlerRegistrar),
      nameof(IStepHandlerRegistry),
      nameof(OperatorExecutionResult),
      nameof(OperatorExecutionFailureException),
      nameof(OperatorRunOutput),
      // Operation items
      nameof(IOperationItemWriter),
      nameof(IOperationItemRetryHandler),
      nameof(OperationGroup),
      nameof(OperationItem),
      nameof(OperationGroupAggregator),
      nameof(OperationItemReporting),
      nameof(OperationFailureCategory),
      nameof(ProcedureExecutionOutcome),
      nameof(ProcedureOutcomeKind),
      // Settings
      nameof(IModuleIntegrationSettingDefinitionContributor),
      nameof(PlatformSettingDefinition),
      // Configuration (SDK-6C)
      nameof(IIntegrationConfigurationContributor),
      nameof(IIntegrationSettingsAccessor),
      nameof(IIntegrationConnectionAccessor),
      nameof(IntegrationSettingDefinition),
      nameof(ConnectionRequirementDefinition),
      nameof(ProcedureRequirementSet),
      nameof(ConfigurationReadinessEvaluator),
      nameof(ConfigurationEvidenceSnapshot),
  ];

    [Fact]
    public void Integration_SDK_exports_expected_public_contract_types()
    {
        var assembly = typeof(IStepHandler).Assembly;
        var publicTypeNames = assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedPublicTypes.Where(t => !publicTypeNames.Contains(t)).ToList();
        Assert.True(missing.Count == 0, $"Missing exported SDK types: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Integration_SDK_execution_types_are_interfaces_or_intentional_helpers()
    {
        var assembly = typeof(IStepHandler).Assembly;
        var executionTypes = assembly.GetExportedTypes()
            .Where(t => t.Namespace?.Contains("Execution", StringComparison.Ordinal) == true)
            .ToList();

        foreach (var type in executionTypes)
        {
            var allowed = type.IsInterface
                || type.IsEnum
                || type.Name.EndsWith("Exception", StringComparison.Ordinal)
                || type.Name is nameof(OperatorExecutionResult) or nameof(OperatorRunOutput);
            Assert.True(allowed, $"{type.FullName} should remain a narrow execution contract.");
        }
    }
}
