using Apperture.Core.Domain.Client;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Query service for available client definitions. Epic 51.
/// </summary>
public interface IClientDefinitionQueryService
{
    /// <summary>
    /// Gets all available client definitions from all providers.
    /// </summary>
    ValueTask<IReadOnlyList<ClientDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a client definition by id, or null if not found.
    /// </summary>
    ValueTask<ClientDefinition?> GetByIdAsync(ClientDefinitionId id, CancellationToken cancellationToken = default);
}
