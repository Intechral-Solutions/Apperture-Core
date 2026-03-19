using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public interface IOperationDefinitionReadAuthorizationService
{
    ValueTask EnsureCanReadDefinitionAsync(OperationId operationId, CancellationToken cancellationToken = default);
}

