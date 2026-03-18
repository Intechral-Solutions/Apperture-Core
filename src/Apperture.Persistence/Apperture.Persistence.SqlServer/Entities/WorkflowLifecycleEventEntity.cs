namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable lifecycle history entry for a workflow run and its control surfaces.
/// </summary>
public sealed class WorkflowLifecycleEventEntity
{
    public int Id { get; set; }
    public string RunId { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string WorkflowDefinitionId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? RelatedRunId { get; set; }
    public string? StepName { get; set; }
    public string? DetailsJson { get; set; }
}
