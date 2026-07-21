using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.Core.Application.Integration;

/// <summary>
/// Aggregates module-authored integration configuration declarations keyed by module id.
/// Modules that only implement legacy setting contributors are absent from this registry.
/// </summary>
public interface IIntegrationConfigurationRegistry
{
    IReadOnlyList<string> GetRegisteredModuleIds();

    bool TryGetModuleConfiguration(string moduleId, out ModuleIntegrationConfiguration configuration);
}

/// <summary>Validated configuration declarations for one module.</summary>
public sealed record ModuleIntegrationConfiguration(
    string ModuleId,
    IReadOnlyList<IntegrationSettingDefinition> Settings,
    IReadOnlyList<ConnectionRequirementDefinition> Connections,
    IReadOnlyList<ProcedureRequirementSet> Procedures);

/// <summary>DI binding of a module id to its configuration contributor.</summary>
public sealed record ModuleIntegrationConfigurationBinding(
    string ModuleId,
    IIntegrationConfigurationContributor Contributor);
