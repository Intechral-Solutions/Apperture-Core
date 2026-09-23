using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Projects Connector definitions into the Component catalog. Depends only on the authorized catalog
/// read service (<c>connector.definition.read</c>), never on the definition registry directly, so
/// aggregation can never grant sight of a definition the family gate refuses.
/// </summary>
public sealed class ConnectorComponentCatalogProvider : IComponentCatalogProvider
{
    private readonly IConnectorDefinitionCatalogReadService _definitions;

    public ConnectorComponentCatalogProvider(IConnectorDefinitionCatalogReadService definitions) =>
        _definitions = definitions;

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
            Connector: new ConnectorComponentFacts(fields, supportsVerification));
    }

    private static ComponentSummary ToSummary(ConnectorDefinition definition) => new(
        new ComponentKey(ComponentKinds.Connector, definition.Id.Value),
        definition.Name,
        definition.Description,
        ComponentOrigins.FromSupplyingModuleId(definition.ModuleId),
        definition.ModuleId);
}
