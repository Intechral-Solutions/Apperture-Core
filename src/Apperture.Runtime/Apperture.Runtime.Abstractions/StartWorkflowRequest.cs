namespace Apperture.Runtime.Abstractions;

public sealed record StartWorkflowRequest(
    WorkflowDefinitionId WorkflowDefinitionId,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId);
