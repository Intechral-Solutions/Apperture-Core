using Apperture.Core.Application.PlatformSettings;

namespace Apperture.UnitTests.PlatformSettings;

internal sealed class TestIntegrationSettingDefinitionRegistry : IIntegrationSettingDefinitionRegistry
{
    private static readonly IReadOnlyList<PlatformSettingDefinition> Definitions =
    [
        PlatformSettingTestDefinitions.IntegrationScopedSelectSample,
        PlatformSettingTestDefinitions.IntegrationScopedMultiSelectSample
    ];

    private static readonly Dictionary<string, PlatformSettingDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public IReadOnlyList<PlatformSettingDefinition> GetAll() => Definitions;

    public PlatformSettingDefinition? TryGet(string key) =>
        ByKey.TryGetValue(key, out var d) ? d : null;
}
