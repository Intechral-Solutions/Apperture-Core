namespace Apperture.Core.Application.PlatformSettings;

public interface IIntegrationSettingDefinitionRegistry
{
    IReadOnlyList<PlatformSettingDefinition> GetAll();

    PlatformSettingDefinition? TryGet(string key);
}
