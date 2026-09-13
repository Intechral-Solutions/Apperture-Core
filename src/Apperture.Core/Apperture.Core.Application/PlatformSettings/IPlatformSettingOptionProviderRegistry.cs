namespace Apperture.Core.Application.PlatformSettings;

public interface IPlatformSettingOptionProviderRegistry
{
    bool TryGet(string providerKey, out IPlatformSettingOptionProvider provider);
}

