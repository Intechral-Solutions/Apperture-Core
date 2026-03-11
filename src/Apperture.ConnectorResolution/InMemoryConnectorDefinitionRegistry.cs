using System.Collections.Concurrent;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ConnectorResolution;

/// <summary>
/// In-memory connector definition registry for runtime resolution.
/// Used when the host does not use the control-plane registry (e.g. worker host).
/// </summary>
public sealed class InMemoryConnectorDefinitionRegistry : IConnectorDefinitionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectorDefinition> _definitions = new();

    /// <inheritdoc />
    public void Register(ConnectorDefinition definition) =>
        _definitions[definition.Id.Value] = definition;

    /// <inheritdoc />
    public ConnectorDefinition? Get(ConnectorDefinitionId id) =>
        _definitions.TryGetValue(id.Value, out var def) ? def : null;

    /// <inheritdoc />
    public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null)
    {
        var list = _definitions.Values.AsEnumerable();
        if (moduleId is not null)
            list = list.Where(d => string.Equals(d.ModuleId, moduleId, StringComparison.Ordinal));
        return list.ToList();
    }
}
