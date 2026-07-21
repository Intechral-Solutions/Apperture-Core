using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Maps <see cref="IIntegrationConfigurationContributor"/> settings onto legacy platform setting definitions.
/// </summary>
public sealed class IntegrationConfigurationSettingContributorAdapter : IModuleIntegrationSettingDefinitionContributor
{
    private readonly string _moduleId;
    private readonly IIntegrationConfigurationContributor _contributor;

    public IntegrationConfigurationSettingContributorAdapter(
        string moduleId,
        IIntegrationConfigurationContributor contributor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(contributor);
        _moduleId = moduleId;
        _contributor = contributor;
    }

    public IReadOnlyList<PlatformSettingDefinition> GetIntegrationSettingDefinitions() =>
        IntegrationConfigurationDeclarations.ToPlatformSettingDefinitions(
            _contributor.GetSettingDefinitions() ?? Array.Empty<IntegrationSettingDefinition>(),
            _moduleId);
}
