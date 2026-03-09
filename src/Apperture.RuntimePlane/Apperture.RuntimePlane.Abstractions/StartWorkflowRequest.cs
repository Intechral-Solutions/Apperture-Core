namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Workflow start request.
/// Parameters that require credentials should carry a secret reference contract, not plaintext values.
/// </summary>
public sealed record StartWorkflowRequest(
    WorkflowDefinitionId WorkflowDefinitionId,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId);
