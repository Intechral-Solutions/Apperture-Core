namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Module contribution of typed integration settings, connection requirements,
/// and optional procedure-specific requirement subsets.
/// </summary>
public interface IIntegrationConfigurationContributor
{
    IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions();

    IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements();

    IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() =>
        Array.Empty<ProcedureRequirementSet>();
}
