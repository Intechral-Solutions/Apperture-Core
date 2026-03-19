namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable lifecycle history entry for an operation run.
/// </summary>
public sealed class OperationLifecycleEventEntity
{
    public int Id { get; set; }
    public string RunId { get; set; } = "";
    public string OperationInstanceId { get; set; } = "";
    public string OperationDefinitionId { get; set; } = "";

    public string EventType { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public string? RelatedRunId { get; set; }
    public string? StepName { get; set; }
    public string? DetailsJson { get; set; }
}

