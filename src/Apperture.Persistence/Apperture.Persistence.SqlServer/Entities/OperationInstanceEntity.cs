namespace Apperture.Persistence.SqlServer;

public sealed class OperationInstanceEntity
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CorrelationKey { get; set; }

    /// <summary>
    /// When status is <c>running</c>, the current handler step name.
    /// </summary>
    public string? CurrentStepName { get; set; }

    public int Version { get; set; }
}

