namespace Apperture.Persistence.SqlServer;

public sealed class OperationRunEntity
{
    public string Id { get; set; } = "";
    public string WorkflowInstanceId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CorrelationKey { get; set; }
}
