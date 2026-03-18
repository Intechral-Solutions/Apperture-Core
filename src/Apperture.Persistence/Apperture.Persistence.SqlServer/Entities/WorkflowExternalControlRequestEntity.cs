namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable external-control request for a workflow run.
/// Control-plane persists intent; the instance-plane worker consumes it at safe boundaries.
/// </summary>
public sealed class WorkflowExternalControlRequestEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string WorkflowDefinitionId { get; set; } = "";
    public string ControlKind { get; set; } = "";
    public string Status { get; set; } = Apperture.RuntimePlane.Abstractions.WorkflowExternalControlStatusNames.Queued;
    public DateTimeOffset RequestedAt { get; set; }
    public string? RequestedBy { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public string? LastError { get; set; }
}
