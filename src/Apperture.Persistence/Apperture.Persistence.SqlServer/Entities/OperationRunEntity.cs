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
    public string? RetryOfRunId { get; set; }

    /// <summary>
    /// Captured output from the last step on successful terminal completion. Serialized JSON or null.
    /// </summary>
    public string? CapturedOutputJson { get; set; }

    /// <summary>
    /// Short human-readable summary when derivable (e.g. string return, or message property).
    /// </summary>
    public string? CapturedOutputSummary { get; set; }

    /// <summary>
    /// Capture status: none, empty, captured, truncated, unsupported. See ADR-0019.
    /// </summary>
    public string? CapturedOutputStatus { get; set; }

    /// <summary>
    /// Schema version for captured output envelope evolution.
    /// </summary>
    public int CapturedOutputVersion { get; set; }

    /// <summary>
    /// Operator display policy when the handler used <see cref="Apperture.Core.Application.Execution.OperatorRunOutput"/>:
    /// inline, summary_only, withheld; null for legacy/plain Epic-20 returns. See ADR-0020.
    /// </summary>
    public string? CapturedOutputDisplayPolicy { get; set; }
}
