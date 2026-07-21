using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.Core.Application.Integration;

/// <inheritdoc />
public sealed class IntegrationConfigurationRegistry : IIntegrationConfigurationRegistry
{
    private readonly Dictionary<string, ModuleIntegrationConfiguration> _byModule;

    public IntegrationConfigurationRegistry(IEnumerable<ModuleIntegrationConfigurationBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        _byModule = new Dictionary<string, ModuleIntegrationConfiguration>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.ModuleId))
                continue;

            IntegrationConfigurationDeclarations.Validate(binding.Contributor);
            _byModule[binding.ModuleId] = new ModuleIntegrationConfiguration(
                binding.ModuleId,
                binding.Contributor.GetSettingDefinitions() ?? Array.Empty<IntegrationSettingDefinition>(),
                binding.Contributor.GetConnectionRequirements() ?? Array.Empty<ConnectionRequirementDefinition>(),
                binding.Contributor.GetProcedureRequirements() ?? Array.Empty<ProcedureRequirementSet>());
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredModuleIds() => _byModule.Keys.ToList();

    /// <inheritdoc />
    public bool TryGetModuleConfiguration(string moduleId, out ModuleIntegrationConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            configuration = null!;
            return false;
        }

        return _byModule.TryGetValue(moduleId, out configuration!);
    }
}
