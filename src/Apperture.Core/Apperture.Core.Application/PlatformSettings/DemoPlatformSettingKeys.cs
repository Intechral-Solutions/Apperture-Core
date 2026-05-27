namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Stable keys for Epic 120 demo/gallery settings. Not production controls.</summary>
public static class DemoPlatformSettingKeys
{
    public const string Boolean = "demo.settings.boolean";
    public const string Integer = "demo.settings.integer";
    public const string Text = "demo.settings.text";
    public const string Duration = "demo.settings.duration";
    public const string StaticSelect = "demo.settings.staticSelect";
    public const string StaticMultiSelect = "demo.settings.staticMultiSelect";
    public const string ProviderSelect = "demo.settings.providerSelect";
    public const string ProviderMultiSelect = "demo.settings.providerMultiSelect";
    public const string RequiresRestart = "demo.settings.requiresRestart";
    public const string ReadOnly = "demo.settings.readOnly";

    public const string ConnectorDefinitionsProviderKey = "platform.connectorDefinitions";
}
