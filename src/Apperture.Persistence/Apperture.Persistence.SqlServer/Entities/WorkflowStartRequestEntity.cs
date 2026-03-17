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
    public DateTimeOffset CreatedAt { get; set; }
}

