using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Module-supplied retry support for durable operation items (SDK-4A).
/// Platform does not infer retry behavior; modules must register a handler.
/// </summary>
public interface IOperationItemRetryHandler
{
    string ModuleId { get; }

    string OperationKind { get; }

    /// <summary>Whether this handler owns the given group/item pair.</summary>
    bool Supports(OperationItem item, OperationGroup group);

    /// <summary>Static eligibility from stored item metadata (no side effects).</summary>
    OperationItemRetryEligibility Evaluate(OperationItem item, OperationGroup group);

    /// <summary>
    /// Builds a child-run start plan when <see cref="Evaluate"/> allows retry.
    /// Throws <see cref="OperationItemizationException"/> when metadata is invalid after Evaluate passed.
    /// </summary>
    OperationItemRetryStartPlan BuildStartPlan(OperationItemRetryBuildContext context);
}

public sealed record OperationItemRetryEligibility(
    bool CanRetry,
    string? BlockReasonCode = null,
    string? BlockReasonMessage = null);

public sealed record OperationItemRetryBuildContext(
    OperationItem Item,
    OperationGroup Group,
    string RetryAttemptId,
    string ClientIdempotencyKey,
    string OriginalRunId);

public sealed record OperationItemRetryStartPlan(
    WorkflowDefinitionId WorkflowDefinitionId,
    IReadOnlyDictionary<string, object?> Parameters,
    string? IntegrationId,
    string? ProcedureId);
