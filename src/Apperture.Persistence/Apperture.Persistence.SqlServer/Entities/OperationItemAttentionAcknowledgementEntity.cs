namespace Apperture.Persistence.SqlServer;

/// <summary>JOB-1 — durable acknowledgement that a failed operation item no longer needs attention.</summary>
public sealed class OperationItemAttentionAcknowledgementEntity
{
    public string Id { get; set; } = "";
    public string OperationItemId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string OperationGroupId { get; set; } = "";
    public string Disposition { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset AcknowledgedAtUtc { get; set; }
    public string AcknowledgedByUserId { get; set; } = "";
    public DateTimeOffset? ReopenedAtUtc { get; set; }
    public string? ReopenedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public OperationItemEntity? Item { get; set; }
}
