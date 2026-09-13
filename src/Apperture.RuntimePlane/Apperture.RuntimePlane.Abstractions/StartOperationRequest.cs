using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

/// <summary>
/// Operation start request payload (control-plane to runtime-plane handoff contract).
/// Parameters must be orchestration-safe (no secret material).
/// </summary>
public sealed record StartOperationRequest(
    /// <summary>
    /// Operation definition id as a string (control plane maps this to the core domain <c>OperationId</c>).
    /// </summary>
    string OperationDefinitionId,
    IReadOnlyDictionary<string, object?>? Parameters,
    string? IdempotencyKey,
    string? CorrelationId,
    /// <summary>Optional integration this run belongs to (parity with workflows; ADR-0029).</summary>
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
    string? ResourceContextId = null);

