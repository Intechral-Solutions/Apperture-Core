namespace Apperture.Persistence.SqlServer;

public sealed class OperationGroupEntity
{
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public string? IntegrationId { get; set; }
    public string ModuleId { get; set; } = "";
    public string? ProcedureId { get; set; }
    public string OperationKind { get; set; } = "";
    public string? SourceLabel { get; set; }
    public string Status { get; set; } = "";
    public int TotalCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? SummaryJson { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<OperationItemEntity> Items { get; set; } = [];
}

public sealed class OperationItemEntity
{
    public string Id { get; set; } = "";
    public string OperationGroupId { get; set; } = "";
    public string RunId { get; set; } = "";
    public int Sequence { get; set; }
    public string? SubjectKey { get; set; }
    public string? DisplayLabel { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsRetryable { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ChangeSummaryJson { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public OperationGroupEntity? Group { get; set; }

    public List<OperationItemRetryAttemptEntity> RetryAttempts { get; set; } = [];
}

/// <summary>SDK-4A — durable append-only retry attempt for a failed operation item.</summary>
public sealed class OperationItemRetryAttemptEntity
{
    public string Id { get; set; } = "";
    public string OperationItemId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string OperationGroupId { get; set; } = "";
    public int AttemptIndex { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string? RequestedByActorId { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "";
    public string? ChildRunId { get; set; }
    public string? ChildOperationGroupId { get; set; }
    public string? ChildOperationItemId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public OperationItemEntity? Item { get; set; }
}
