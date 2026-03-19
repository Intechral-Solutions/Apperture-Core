using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorizes operation start based on definition-level <c>OperationDefinition.RequiredPermissions</c>,
/// with coarse fallback when no required permissions are declared.
/// </summary>
public interface IOperationStartAuthorizationService
{
    /// <summary>
    /// Ensures the current actor is allowed to start the given operation. Throws <see cref="UnauthorizedAccessException"/> if not.
    /// </summary>
    ValueTask EnsureCanStartOperationAsync(OperationId operationId, CancellationToken cancellationToken = default);
}

