namespace Apperture.Persistence.SqlServer;

public sealed class WorkflowInstanceEntity
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CorrelationKey { get; set; }
    public string? CurrentStepName { get; set; }
    public int Version { get; set; }
}
