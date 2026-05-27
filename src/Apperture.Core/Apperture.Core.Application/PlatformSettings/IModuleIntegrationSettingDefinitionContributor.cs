namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Optional module contribution of integration-scoped platform setting definitions.
/// Registered from known <see cref="Apperture.ModuleSdk.Abstractions.IModule"/> instances at host startup.
/// </summary>
public interface IModuleIntegrationSettingDefinitionContributor
{
    IReadOnlyList<PlatformSettingDefinition> GetIntegrationSettingDefinitions();
}
