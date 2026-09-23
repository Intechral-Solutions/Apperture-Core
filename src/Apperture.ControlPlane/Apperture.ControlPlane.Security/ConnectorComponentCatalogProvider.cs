using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Projects Connector definitions into the Component catalog. Depends only on the authorized services: the
/// catalog read service (<c>connector.definition.read</c>) for the definitions and the connection management
/// service (<c>connection.read</c>) for usage, never on the definition registry directly, so aggregation can
/// never grant sight of anything the family gates refuse.
/// </summary>
public sealed class ConnectorComponentCatalogProvider : IComponentCatalogProvider
{
    /// <summary>How many Connections detail names; the count itself is always complete.</summary>
    public const int MaxNamedConnections = 25;

    private readonly IConnectorDefinitionCatalogReadService _definitions;
    private readonly IConnectionManagementService _connections;

    public ConnectorComponentCatalogProvider(
        IConnectorDefinitionCatalogReadService definitions,
        IConnectionManagementService connections)
    {
        _definitions = definitions;
        _connections = connections;
    }

    public string Kind => ComponentKinds.Connector;

    public async ValueTask<IReadOnlyList<ComponentSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _definitions.ListAsync(cancellationToken).ConfigureAwait(false);
        return definitions.Select(ToSummary).ToList();
    }

    public async ValueTask<ComponentDetail?> GetAsync(string nativeId, CancellationToken cancellationToken = default)
    {
        var definition = await _definitions.GetByIdAsync(new ConnectorDefinitionId(nativeId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return null;

        var fields = definition.ConnectionFields
            .Select(f => new ConnectorComponentField(
                f.Key,
                f.DisplayName,
                f.IsSecret,
                f.Description,
                definition.RequiredInputSchema.ContainsKey(f.Key)))
            .ToList();
        // BrokeredAccessCandidate is documented as reserved/future and is deliberately not surfaced.
        var supportsVerification = definition.Capabilities.HasFlag(ConnectorCapabilities.SupportsConnectionVerification);

        return new ComponentDetail(
            ToSummary(definition),
            Connector: new ConnectorComponentFacts(fields, supportsVerification),
            Usage: await GetUsageAsync(definition.Id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Active Connections made from the connector. Null (not zero) when the caller may not read Connections,
    /// so an unprivileged caller is never told "this connector is unused".
    /// </summary>
    private async ValueTask<ComponentUsage?> GetUsageAsync(ConnectorDefinitionId id, CancellationToken cancellationToken)
    {
        IReadOnlyList<Connection> connections;
        try
        {
            connections = await _connections.ListConnectionsByConnectorAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // Usage means Active Connections: a retired Connection is excluded from binding eligibility and is
        // decommissioned configuration, so counting it would make an idle connector look in use. The connector
        // match is re-checked so the result never depends on the query having been honoured.
        var active = connections
            .Where(c => c.State == ConnectionState.Active && c.ConnectorDefinitionId == id)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal)
            .ToList();

        return new ComponentUsage(
            Connector: new ConnectorComponentUsage(
                active.Count,
                active.Take(MaxNamedConnections).Select(c => new ComponentConnectionRef(c.Id.Value, c.Name)).ToList()));
    }

    private static ComponentSummary ToSummary(ConnectorDefinition definition) => new(
        new ComponentKey(ComponentKinds.Connector, definition.Id.Value),
        definition.Name,
        definition.Description,
        ComponentOrigins.FromSupplyingModuleId(definition.ModuleId),
        definition.ModuleId);
}
