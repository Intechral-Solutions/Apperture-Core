namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Merges platform-owned and module-contributed integration setting definitions.</summary>
public sealed class CompositeIntegrationSettingDefinitionRegistry : IIntegrationSettingDefinitionRegistry
{
    private readonly IReadOnlyList<PlatformSettingDefinition> _definitions;
    private readonly Dictionary<string, PlatformSettingDefinition> _byKey;

    public CompositeIntegrationSettingDefinitionRegistry(
        IntegrationSettingDefinitionRegistry platformRegistry,
        IEnumerable<IModuleIntegrationSettingDefinitionContributor> moduleContributors)
    {
        var list = new List<PlatformSettingDefinition>(platformRegistry.GetAll());
        foreach (var contributor in moduleContributors)
            list.AddRange(contributor.GetIntegrationSettingDefinitions());

        _definitions = list;
        _byKey = list.ToDictionary(d => d.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<PlatformSettingDefinition> GetAll() => _definitions;

    public PlatformSettingDefinition? TryGet(string key) =>
        _byKey.TryGetValue(key, out var d) ? d : null;
}
