using Apperture.Core.Domain.Client;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Authorized read surface for client definitions. Epic 51.
/// </summary>
public interface IClientDefinitionReadService
{
    ValueTask<IReadOnlyList<ClientDefinition>> ListAsync(CancellationToken cancellationToken = default);
    ValueTask<ClientDefinition?> GetByIdAsync(ClientDefinitionId id, CancellationToken cancellationToken = default);
}
