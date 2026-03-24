using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Authorized read of the connector definition catalog for management surfaces.
/// </summary>
public interface IConnectorDefinitionCatalogReadService
{
    ValueTask<IReadOnlyList<ConnectorDefinition>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ConnectorDefinition?> GetByIdAsync(ConnectorDefinitionId id, CancellationToken cancellationToken = default);
}
