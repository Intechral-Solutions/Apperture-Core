using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for binding resources. Used by binding service.
/// </summary>
public interface IBindingRepository
{
    /// <summary>
    /// Gets the binding for a slot. Returns null if none exists.
    /// Legacy: returns first match for (moduleId, slotName). Integration-scoped resolution is deferred.
    /// </summary>
    ValueTask<Binding?> GetBySlotAsync(string moduleId, string slotName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all bindings owned by the given Integration. Epic 50.
    /// </summary>
    ValueTask<IReadOnlyList<Binding>> GetByIntegrationIdAsync(IntegrationId integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the binding for a slot within an Integration. Returns null if none exists.
    /// </summary>
    ValueTask<Binding?> GetBySlotAndIntegrationAsync(string moduleId, string slotName, IntegrationId integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a binding by id.
    /// </summary>
    ValueTask<Binding?> GetByIdAsync(BindingId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all bindings that reference the given connection. Used for Used In projection.
    /// </summary>
    ValueTask<IReadOnlyList<Binding>> GetByConnectionIdAsync(ConnectionId connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new binding.
    /// </summary>
    ValueTask AddAsync(Binding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing binding (e.g. state).
    /// </summary>
    ValueTask UpdateAsync(Binding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a binding.
    /// </summary>
    ValueTask DeleteAsync(BindingId id, CancellationToken cancellationToken = default);
}
