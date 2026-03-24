using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Runtime slot resolver: resolves only via (moduleId, slotName) using binding and connection lookup.
/// No connection-by-id; binding is the gate.
/// Integration-scoped resolution is deferred: when multiple bindings exist for a slot (Integration-owned
/// and legacy), the repository returns the first match. No Integration-aware selection is performed.
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
    public async ValueTask<T?> ResolveSlotAsync<T>(string moduleId, string slotName, CancellationToken cancellationToken = default) where T : class
    {
        var binding = await _bindingRepository.GetBySlotAsync(moduleId, slotName, cancellationToken);
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
