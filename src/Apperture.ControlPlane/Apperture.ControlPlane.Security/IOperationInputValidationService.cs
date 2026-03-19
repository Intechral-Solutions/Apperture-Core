using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public interface IOperationInputValidationService
{
    ValueTask ValidateAsync(OperationId operationId, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken = default);
}

