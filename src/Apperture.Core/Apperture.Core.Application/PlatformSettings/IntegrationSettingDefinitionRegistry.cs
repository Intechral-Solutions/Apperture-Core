namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Platform-owned integration-scoped setting definitions. Module demos merge via <see cref="CompositeIntegrationSettingDefinitionRegistry"/>.</summary>
public sealed class IntegrationSettingDefinitionRegistry : IIntegrationSettingDefinitionRegistry
{
    private static readonly IReadOnlyList<PlatformSettingDefinition> Definitions = [];

    private static readonly Dictionary<string, PlatformSettingDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public IReadOnlyList<PlatformSettingDefinition> GetAll() => Definitions;

    public PlatformSettingDefinition? TryGet(string key) =>
        ByKey.TryGetValue(key, out var d) ? d : null;
}
