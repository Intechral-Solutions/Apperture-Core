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
    string? ExecutionOrigin = null,
    /// <summary>
    /// CTX-2: pre-resolved ResourceContext for the occurrence about to be created (e.g. the firing
    /// Schedule's context). When supplied, the dispatch service snapshots it as-is rather than
    /// deriving one from <see cref="IntegrationId"/>. Omit for manual/Integration-scoped starts —
    /// the dispatch service resolves it from the Integration's current ResourceContext instead.
    /// </summary>
    string? ResourceContextId = null,
    /// <summary>
    /// Who requested this start, as the platform recorded it: a user id, or a non-human actor literal such as
    /// <c>system:scheduler</c>. Set only by server-side code from the audited unit's own actor or a system subsystem, never from
    /// request input. Persisted on the start request row; the worker that later claims the row is recorded separately
    /// (<c>ClaimedBy</c>).
    /// </summary>
    string? RequestedBy = null);
