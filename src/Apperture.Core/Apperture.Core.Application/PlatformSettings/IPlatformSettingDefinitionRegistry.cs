namespace Apperture.Core.Application.PlatformSettings;

public interface IPlatformSettingDefinitionRegistry
{
    IReadOnlyList<PlatformSettingDefinition> GetAll();

    PlatformSettingDefinition? TryGet(string key);
}
