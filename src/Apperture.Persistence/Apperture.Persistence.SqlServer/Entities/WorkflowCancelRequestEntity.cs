namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable cancel intent for a workflow run. Control plane persists intent; instance plane observes and honors cooperatively.
/// </summary>
public sealed class WorkflowCancelRequestEntity
{
    public string RunId { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string WorkflowDefinitionId { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public string? RequestedBy { get; set; }
}

