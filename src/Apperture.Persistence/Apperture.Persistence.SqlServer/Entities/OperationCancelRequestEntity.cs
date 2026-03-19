namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable cancel intent for an operation run.
/// </summary>
public sealed class OperationCancelRequestEntity
{
    public string RunId { get; set; } = "";
    public string OperationInstanceId { get; set; } = "";
    public string OperationDefinitionId { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public string? RequestedBy { get; set; }
}

