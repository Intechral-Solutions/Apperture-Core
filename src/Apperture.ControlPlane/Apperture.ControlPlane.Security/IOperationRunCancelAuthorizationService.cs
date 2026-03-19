using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public interface IOperationRunCancelAuthorizationService
{
    /// <summary>
    /// Ensures the current actor is allowed to cancel the given operation run.
    /// Throws <see cref="UnauthorizedAccessException"/> when not authorized.
    /// </summary>
    ValueTask EnsureCanCancelRunAsync(OperationId operationId, CancellationToken cancellationToken = default);
}

