using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Evaluates which connections are eligible for binding to a slot.
/// </summary>
public interface IBindingEligibilityService
{
    /// <summary>
    /// Gets connections eligible for binding to the specified slot.
    /// Filters by active state, contract type, and scope visibility.
    /// </summary>
    ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
        string moduleId,
        string slotName,
        string contractType,
        CancellationToken cancellationToken = default);
}
