using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public interface IOperationRunReadAuthorizationService
{
    /// <summary>
    /// Ensures the current actor is allowed to read operation runs for the given operation.
    /// Throws <see cref="UnauthorizedAccessException"/> when not authorized.
    /// </summary>
    ValueTask EnsureCanReadRunsAsync(OperationId operationId, CancellationToken cancellationToken = default);
}

