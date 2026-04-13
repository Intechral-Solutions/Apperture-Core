using Apperture.Core.Domain.Operations;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Core.Application.Services;

public interface IOperationStartDispatchService
{
    ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default);
}

public sealed record OperationStartDispatchResult(
    OperationRunId RunId,
    OperationInstanceId OperationInstanceId,
    OperationId OperationDefinitionId,
    string Status);
