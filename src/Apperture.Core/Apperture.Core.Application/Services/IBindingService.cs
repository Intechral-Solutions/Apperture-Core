using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Manages bindings: create/delete slot-to-connection delegations; resolve slot to bound connection/capability.
/// Epic 50: IntegrationId for Integration-owned bindings.
/// </summary>
public interface IBindingService
{
    /// <summary>
    /// Creates a binding from a slot to a connection, owned by the given Integration (Epic 54).
    /// Rejects a Connection that is neither in the Integration's ResourceContext nor in Shared.
    /// </summary>
    ValueTask<Binding> CreateBindingAsync(
        string moduleId,
        string slotName,
        string contractType,
        ConnectionId connectionId,
        IntegrationId integrationId,
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
    /// Resolves the slot binding that is not owned by any Integration. Returns null if none exists.
    /// </summary>
    /// <remarks>
    /// This is the module-scoped legacy surface. It deliberately cannot see Integration-owned bindings:
    /// resolving one of those without the Integration identity that disambiguates it would let a caller
    /// receive an arbitrary Integration's binding. Use the Integration-scoped surface for those.
    /// </remarks>
    ValueTask<Binding?> GetBindingAsync(
        string moduleId,
        string slotName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections eligible for binding to the slot. Requires binding.assign permission.
    /// </summary>
    /// <param name="owningResourceContextId">
    /// ResourceContext of the Integration that will own the binding, or null when the caller has no
    /// owning Integration (module-scoped legacy discovery).
    /// </param>
    ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        ResourceContextId? owningResourceContextId = null,
        CancellationToken cancellationToken = default);
}
