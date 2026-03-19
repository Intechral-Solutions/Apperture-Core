namespace Apperture.Persistence.SqlServer;

public sealed class OperationStepInstanceEntity
{
    public string OperationInstanceId { get; set; } = "";
    public string StepName { get; set; } = "";
    public int Attempt { get; set; }

    public string Status { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Error { get; set; }
}

