using Apperture.Core.Domain.Workflow;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted operation start request payload (control-plane to instance-plane handoff).
/// </summary>
public sealed class OperationStartRequestEntity
{
    public string RunId { get; set; } = "";
    public string OperationInstanceId { get; set; } = "";
    public string OperationDefinitionId { get; set; } = "";

    public string? ParametersJson { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>Integration that owns this start request, when known (ADR-0029).</summary>
    public string? IntegrationId { get; set; }

    /// <summary>Module procedure that launched this request, when known (ADR-0029).</summary>
    public string? ProcedureId { get; set; }

    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot resolved once at dispatch, before this row is
    /// written. Never re-derived from current Integration/Schedule ownership. Null for pre-CTX-2
    /// rows and for legitimately contextless (System) starts.
    /// </summary>
    public string? ResourceContextId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Status { get; set; } = WorkflowLifecycleStatusNames.Requested;
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public string? LastError { get; set; }
}

