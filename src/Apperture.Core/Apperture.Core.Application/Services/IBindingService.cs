using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Manages bindings: create/delete slot-to-connection delegations; resolve slot to bound connection/capability.
/// Epic 50: IntegrationId for Integration-owned bindings.
/// </summary>
public interface IBindingService
{
    /// <summary>
    /// Creates a binding from a slot to a connection.
    /// When integrationId is provided, the binding is owned by that Integration (moduleId from Integration).
    /// </summary>
    ValueTask<Binding> CreateBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        ConnectionId connectionId,
        IntegrationId? integrationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a binding from a slot to a module-provided capability.
    /// </summary>
    ValueTask<Binding> CreateCapabilityBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        string capabilityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes (revokes) a binding.
    /// </summary>
    ValueTask DeleteBindingAsync(BindingId bindingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the binding for a slot. Returns null if no active binding exists.
    /// </summary>
    ValueTask<Binding?> GetBindingAsync(
        string moduleId,
        string slotName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections eligible for binding to the slot. Requires binding.assign permission.
    /// </summary>
    ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        CancellationToken cancellationToken = default);
}
