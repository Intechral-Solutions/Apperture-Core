using Apperture.Core.Domain.Client;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Provides client definitions contributed by modules or connectors. Epic 51.
/// </summary>
public interface IClientDefinitionProvider
{
    /// <summary>
    /// Enumerates client definitions contributed by this provider.
    /// </summary>
    IAsyncEnumerable<ClientDefinition> GetClientDefinitionsAsync(CancellationToken cancellationToken = default);
}
