using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

public sealed record OperationRun(
    OperationRunId Id,
    WorkflowInstanceId WorkflowInstanceId,
    WorkflowDefinitionId DefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationKey);
