using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Authorized read of the connector definition catalog for management surfaces.
/// Data comes from <see cref="IConnectorDefinitionRegistry"/> (code/host registration), not from EF or SQL.
/// </summary>
public interface IConnectorDefinitionCatalogReadService
{
    ValueTask<IReadOnlyList<ConnectorDefinition>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ConnectorDefinition?> GetByIdAsync(ConnectorDefinitionId id, CancellationToken cancellationToken = default);
}
