using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for binding resources. Used by binding service.
/// </summary>
public interface IBindingRepository
{
    /// <summary>
    /// Gets the binding for a slot that is <b>not owned by any Integration</b> (<c>IntegrationId</c> is
    /// null). Returns null if none exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only contextless slot lookup the platform supports. It exists for genuinely global
    /// bindings — capability bindings created without an Integration, and pre-Integration legacy rows.
    /// </para>
    /// <para>
    /// It must never return an Integration-owned binding. Selecting the first instance-wide match for
    /// <c>(ModuleId, SlotName)</c> would let one Integration resolve another Integration's Connection as
    /// soon as two Integrations of the same module exist. Integration-scoped resolution requires the
    /// Integration identity that disambiguates it: use
    /// <see cref="GetBySlotAndIntegrationAsync"/>.
    /// </para>
    /// </remarks>
    ValueTask<Binding?> GetUnownedBySlotAsync(string moduleId, string slotName, CancellationToken cancellationToken = default);

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
    /// Gets a binding by id and, on a relational provider, locks its row (<c>UPDLOCK, ROWLOCK</c>) until the ambient transaction
    /// ends, so a second delete of the same binding waits and then finds it gone. Untracked, so an execution-strategy rerun reads
    /// afresh. Must be called inside an open transaction. AUD-1.4 slice 4.
    /// </summary>
    ValueTask<Binding?> GetByIdForUpdateAsync(BindingId id, CancellationToken cancellationToken = default);

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
