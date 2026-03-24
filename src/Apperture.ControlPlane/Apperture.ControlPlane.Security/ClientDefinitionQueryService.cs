using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Aggregates client definitions from all registered providers. Epic 51.
/// </summary>
public sealed class ClientDefinitionQueryService : IClientDefinitionQueryService
{
    private readonly IEnumerable<IClientDefinitionProvider> _providers;

    public ClientDefinitionQueryService(IEnumerable<IClientDefinitionProvider> providers)
    {
        _providers = providers ?? Array.Empty<IClientDefinitionProvider>();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ClientDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ClientDefinition>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            await foreach (var def in provider.GetClientDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (seenIds.Add(def.Id.Value))
                    list.Add(def);
            }
        }

        return list;
    }

    /// <inheritdoc />
    public async ValueTask<ClientDefinition?> GetByIdAsync(ClientDefinitionId id, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(d => string.Equals(d.Id.Value, id.Value, StringComparison.Ordinal));
    }
}
