using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates which connections are eligible for binding to a slot.
/// </summary>
public interface IBindingEligibilityService
{
    /// <summary>
    /// Gets connections eligible for binding to the specified slot.
    /// Filters by active state, contract type, scope visibility, and the ResourceContext reference rule.
    /// </summary>
    /// <param name="owningResourceContextId">
    /// The ResourceContext of the Integration that will own the binding. When supplied, only Connections
    /// in that same context or in the reserved Shared context are eligible. When null there is no owning
    /// context to compare against and no context filter is applied — discovery without an owner is not a
    /// substitute for the authoritative check in the binding mutation path.
    /// </param>
    ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        ResourceContextId? owningResourceContextId = null,
        CancellationToken cancellationToken = default);
}
