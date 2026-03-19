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

    public DateTimeOffset CreatedAt { get; set; }

    public string Status { get; set; } = Apperture.RuntimePlane.Abstractions.WorkflowLifecycleStatusNames.Requested;
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public string? LastError { get; set; }
}

