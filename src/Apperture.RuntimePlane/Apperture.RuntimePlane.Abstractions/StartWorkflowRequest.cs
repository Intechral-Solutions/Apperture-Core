namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Workflow start request.
/// Parameters that require credentials should carry a secret reference contract, not plaintext values.
/// </summary>
/// <remarks>
/// Parameters must not contain ResolvedSecret or SecretMaterial. Use SecretRef-shaped values only.
/// Orchestration state must never persist resolved secret material (e.g. in Durable Functions history).
/// </remarks>
public sealed record StartWorkflowRequest(
    WorkflowDefinitionId WorkflowDefinitionId,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId,
    string? RetryOfRunId = null,
    /// <summary>Optional integration this run belongs to (Epic 54); used for slot resolution and operator lineage.</summary>
    string? IntegrationId = null,
    /// <summary>Optional module procedure that launched this run (ADR-0029).</summary>
    string? ProcedureId = null,
    /// <summary><see cref="Apperture.Core.Domain.Workflow.RunExecutionOriginNames"/> when known (Epic 64).</summary>
    string? ExecutionOrigin = null);
