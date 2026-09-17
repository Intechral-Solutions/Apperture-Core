using Apperture.Core.Application.PlatformSettings;

namespace Apperture.UnitTests.PlatformSettings;

internal static class PlatformSettingTestDefinitions
{
    public const string MultiSelectKey = "Test.MultiSelect.Sample";

    public const string ProviderKey = "platform.connectorDefinitions";

    public const string ProviderBackedSelectKey = "Test.ProviderBacked.Select.Sample";

    public const string ProviderBackedMultiSelectKey = "Test.ProviderBacked.MultiSelect.Sample";

    public const string IntegrationProviderKey = "test.integration.options";

    public const string IntegrationProviderBackedSelectKey = "Test.IntegrationProviderBacked.Select.Sample";

    public const string CampusNexusProviderKey = "campusnexus.campuses";

    public const string CampusNexusProviderBackedSelectKey = "Test.CampusNexus.Campuses.Select.Sample";

    public const string IntegrationScopedSelectKey = "Test.IntegrationScoped.Select.Sample";

    public const string IntegrationScopedMultiSelectKey = "Test.IntegrationScoped.MultiSelect.Sample";

    /// <summary>CTX-3B Plan 6a Task 5: a plain (non-provider-backed, non-sensitive) integration-scoped key, so the Integration hard-delete cleanup test can set a value without dragging an option provider in.</summary>
    public const string IntegrationScopedTextKey = "Test.IntegrationScoped.Text.Sample";

    /// <summary>CTX-3B Plan 6a Task 5: the first sensitive integration-scoped definition anywhere in the fixtures. Required to prove Integration hard-delete disposes of the backing secret instead of orphaning it.</summary>
    public const string IntegrationScopedSecretKey = "Test.IntegrationScoped.Secret.Sample";

    private static readonly PlatformSettingValueScopeKind[] IntegrationValueScopes =
        [PlatformSettingValueScopeKind.Integration];

    public static PlatformSettingDefinition MultiSelectSample => new()
    {
        Key = MultiSelectKey,
        DisplayName = "Sample multi-select",
        Description = "Test-only multi-select setting for Epic 112 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.StringArray,
        InputKind = PlatformSettingInputKind.MultiSelect,
        ConfigurationPath = "Test:MultiSelect:Sample",
        DefaultValue = new[] { "alpha" },
        AllowedValues =
        [
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha", Description = "First option" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
            new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
        ],
        MinItems = 0,
        MaxItems = 2
    };

    public static PlatformSettingDefinition ProviderBackedSelectSample => new()
    {
        Key = ProviderBackedSelectKey,
        DisplayName = "Provider-backed select (test-only)",
        Description = "Test-only provider-backed select setting for Epic 114 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Select,
        OptionProviderKey = ProviderKey,
        ConfigurationPath = "Test:ProviderBacked:Select:Sample",
        DefaultValue = "alpha"
    };

    public static PlatformSettingDefinition ProviderBackedMultiSelectSample => new()
    {
        Key = ProviderBackedMultiSelectKey,
        DisplayName = "Provider-backed multi-select (test-only)",
        Description = "Test-only provider-backed multi-select setting for Epic 114 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.StringArray,
        InputKind = PlatformSettingInputKind.MultiSelect,
        OptionProviderKey = ProviderKey,
        ConfigurationPath = "Test:ProviderBacked:MultiSelect:Sample",
        DefaultValue = new[] { "alpha" },
        MinItems = 0,
        MaxItems = 2
    };

    public static PlatformSettingDefinition IntegrationProviderBackedSelectSample => new()
    {
        Key = IntegrationProviderBackedSelectKey,
        DisplayName = "Integration provider-backed select (test-only)",
        Description = "Test-only integration-context provider-backed select setting for Epic 115 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Select,
        OptionProviderKey = IntegrationProviderKey,
        OptionProviderContextKind = PlatformSettingOptionProviderContextKind.Integration,
        ConfigurationPath = "Test:IntegrationProviderBacked:Select:Sample",
        DefaultValue = "int-test:alpha"
    };

    public static PlatformSettingDefinition CampusNexusProviderBackedSelectSample => new()
    {
        Key = CampusNexusProviderBackedSelectKey,
        DisplayName = "CampusNexus campuses select (test-only)",
        Description = "Test-only Campus Nexus campus provider setting for Epic 116 validation.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Select,
        OptionProviderKey = CampusNexusProviderKey,
        OptionProviderContextKind = PlatformSettingOptionProviderContextKind.Integration,
        ConfigurationPath = "Test:CampusNexus:Campuses:Select:Sample",
        DefaultValue = "1"
    };

    public static PlatformSettingDefinition IntegrationScopedSelectSample => new()
    {
        Key = IntegrationScopedSelectKey,
        DisplayName = "Integration-scoped select (test-only)",
        Description = "Test-only integration value scope with integration-context provider for Epic 118.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Select,
        OptionProviderKey = IntegrationProviderKey,
        OptionProviderContextKind = PlatformSettingOptionProviderContextKind.Integration,
        AllowedValueScopes = IntegrationValueScopes,
        ConfigurationPath = "Test:IntegrationScoped:Select:Sample",
        DefaultValue = "int-test:alpha"
    };

    /// <summary>
    /// Ordinary (non-sensitive) integration-scoped string setting with no option provider, mirroring
    /// <see cref="IntegrationScopedSelectSample"/>'s value scope but validating nothing beyond its type.
    /// </summary>
    public static PlatformSettingDefinition IntegrationScopedTextSample => new()
    {
        Key = IntegrationScopedTextKey,
        DisplayName = "Integration-scoped text (test-only)",
        Description = "Test-only plain integration-scoped string setting for CTX-3B delete cleanup.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Text,
        AllowedValueScopes = IntegrationValueScopes,
        ConfigurationPath = "Test:IntegrationScoped:Text:Sample",
        DefaultValue = string.Empty
    };

    /// <summary>
    /// Sensitive integration-scoped setting: the shape of <see cref="IntegrationScopedSelectSample"/>'s
    /// value scope combined with the shape of the real sensitive SMTP-password definition
    /// (<c>IsSensitive</c> + <see cref="PlatformSettingInputKind.Secret"/>), so the stored row holds only
    /// an opaque <c>ISecretStore</c> reference.
    /// </summary>
    public static PlatformSettingDefinition IntegrationScopedSecretSample => new()
    {
        Key = IntegrationScopedSecretKey,
        DisplayName = "Integration-scoped secret (test-only)",
        Description = "Test-only sensitive integration-scoped setting for CTX-3B delete secret disposal.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Secret,
        IsSensitive = true,
        AllowedValueScopes = IntegrationValueScopes,
        ConfigurationPath = "Test:IntegrationScoped:Secret:Sample",
        DefaultValue = string.Empty
    };

    public static PlatformSettingDefinition IntegrationScopedMultiSelectSample => new()
    {
        Key = IntegrationScopedMultiSelectKey,
        DisplayName = "Integration-scoped multi-select (test-only)",
        Description = "Test-only integration value scope multi-select for Epic 118.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.StringArray,
        InputKind = PlatformSettingInputKind.MultiSelect,
        AllowedValueScopes = IntegrationValueScopes,
        ConfigurationPath = "Test:IntegrationScoped:MultiSelect:Sample",
        DefaultValue = new[] { "alpha" },
        AllowedValues =
        [
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
            new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
        ],
        MinItems = 0,
        MaxItems = 2
    };
}
