using Apperture.Core.Application.Services;
using Apperture.Core.Application.Validation;
using Apperture.Core.Domain.Operations;

namespace Apperture.ControlPlane.Security;

public sealed class OperationInputValidationService : IOperationInputValidationService
{
    private readonly IOperationRegistryService _operations;

    public OperationInputValidationService(IOperationRegistryService operations)
    {
        _operations = operations;
    }

    public async ValueTask ValidateAsync(OperationId operationId, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken = default)
    {
        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition is null || string.IsNullOrWhiteSpace(definition.ParameterSchema))
            return;

        OrchestrationParameterSchemaValidator.Validate(definition.ParameterSchema, parameters);
    }
}
