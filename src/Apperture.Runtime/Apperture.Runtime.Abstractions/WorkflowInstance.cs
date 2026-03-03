using Apperture.Shared.Kernel;

namespace Apperture.Runtime.Abstractions;

public sealed record WorkflowInstance(
    WorkflowInstanceId Id,
    WorkflowDefinitionId DefinitionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CorrelationKey,
    string? CurrentStepName,
    int Version);
