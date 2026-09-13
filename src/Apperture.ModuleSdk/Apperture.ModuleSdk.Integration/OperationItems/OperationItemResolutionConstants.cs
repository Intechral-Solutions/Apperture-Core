namespace Apperture.Core.Application.OperationItems;

/// <summary>JOB-1 — bulk / selective retry result categories (partial success supported).</summary>
public static class BulkRetryOperationItemResultCategories
{
    public const string Dispatched = "Dispatched";
    public const string AlreadyRequested = "AlreadyRequested";
    public const string NotRetryable = "NotRetryable";
    public const string NotFailed = "NotFailed";
    public const string Acknowledged = "Acknowledged";
    public const string ItemNotFound = "ItemNotFound";
    public const string WrongGroup = "WrongGroup";
    public const string DispatchFailed = "DispatchFailed";
    public const string ModuleUnsupported = "ModuleUnsupported";
    public const string Blocked = "Blocked";
}

/// <summary>JOB-1 — audit action names for failed-work resolution.</summary>
public static class OperationItemResolutionAuditActions
{
    public const string BulkRetryRequested = "execution.operation_item.bulk_retry.requested";
    public const string BulkRetryCompleted = "execution.operation_item.bulk_retry.completed";
    public const string AttentionAcknowledged = "execution.operation_item.attention.acknowledged";
    public const string AttentionReopened = "execution.operation_item.attention.reopened";
}
