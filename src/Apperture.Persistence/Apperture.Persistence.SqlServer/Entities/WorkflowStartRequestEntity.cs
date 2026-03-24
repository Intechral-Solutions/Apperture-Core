namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Persisted workflow start request payload at the control-plane boundary.
/// This is the durable handoff record that can later be consumed by the instance plane.
/// </summary>
public sealed class WorkflowStartRequestEntity
{
    public string RunId { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string WorkflowDefinitionId { get; set; } = "";
    public string? ParametersJson { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>Integration that owns this start request, when known (Epic 54).</summary>
    public string? IntegrationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Status { get; set; } = Apperture.RuntimePlane.Abstractions.WorkflowLifecycleStatusNames.Requested;
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public string? LastError { get; set; }
}

