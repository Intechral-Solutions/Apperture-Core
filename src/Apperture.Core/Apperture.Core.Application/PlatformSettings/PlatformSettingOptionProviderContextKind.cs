namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Identifies where option values come from for a provider-backed setting.
/// This is provider context, not setting value storage scope.
/// </summary>
public enum PlatformSettingOptionProviderContextKind
{
    Instance = 0,
    Integration = 1
}
