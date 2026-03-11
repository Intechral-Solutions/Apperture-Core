using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for binding resources. Used by binding service.
/// </summary>
public interface IBindingRepository
{
    /// <summary>
    /// Gets the binding for a slot. Returns null if none exists.
    /// </summary>
    ValueTask<Binding?> GetBySlotAsync(string moduleId, string slotName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a binding by id.
    /// </summary>
    ValueTask<Binding?> GetByIdAsync(BindingId id, CancellationToken cancellationToken = default);

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
