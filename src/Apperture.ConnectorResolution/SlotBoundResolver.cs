using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Runtime slot resolver: resolves via binding and connection lookup. When <see cref="IntegrationId"/> is provided,
/// uses integration-scoped bindings; otherwise uses legacy (moduleId, slotName) lookup.
/// </summary>
public sealed class SlotBoundResolver : IRuntimeSlotResolver
{
    private readonly IBindingRepository _bindingRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IConnectionResolver _connectionResolver;

    public SlotBoundResolver(
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IConnectionResolver connectionResolver)
    {
        _bindingRepository = bindingRepository ?? throw new ArgumentNullException(nameof(bindingRepository));
        _connectionRepository = connectionRepository ?? throw new ArgumentNullException(nameof(connectionRepository));
        _connectionResolver = connectionResolver ?? throw new ArgumentNullException(nameof(connectionResolver));
    }

    /// <inheritdoc />
    public ValueTask<T?> ResolveSlotAsync<T>(string moduleId, string slotName, CancellationToken cancellationToken = default) where T : class =>
        ResolveSlotAsync<T>(moduleId, slotName, integrationId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<T?> ResolveSlotAsync<T>(
        string moduleId,
        string slotName,
        string? integrationId,
        CancellationToken cancellationToken = default) where T : class
    {
        Binding? binding;
        if (!string.IsNullOrWhiteSpace(integrationId))
        {
            binding = await _bindingRepository.GetBySlotAndIntegrationAsync(
                moduleId,
                slotName,
                new IntegrationId(integrationId),
                cancellationToken);
        }
        else
        {
            binding = await _bindingRepository.GetBySlotAsync(moduleId, slotName, cancellationToken);
        }

        if (binding is null || binding.State != BindingState.Active)
            return null;

        if (binding.ConnectionId is null)
            return null; // Capability-only binding; no connection to resolve

        var connection = await _connectionRepository.GetByIdAsync(binding.ConnectionId!, cancellationToken);
        if (connection is null)
            return null;

        if (connection.State != ConnectionState.Active)
            return null;

        return await _connectionResolver.ResolveAsync<T>(connection, cancellationToken);
    }
}
