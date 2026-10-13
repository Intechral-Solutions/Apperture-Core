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

    /// <summary>
    /// The platform could not establish whether this item's retry was committed or rolled back. It may or may not have been
    /// requested; the operator reloads the item's retry attempts before asking again.
    /// </summary>
    public const string OutcomeUnknown = "OutcomeUnknown";
}

/// <summary>JOB-1 — audit action names for failed-work resolution.</summary>
public static class OperationItemResolutionAuditActions
{
    /// <summary>Historical only: nothing writes this action any more. Rows already written keep it (catalog entry marked retired).</summary>
    public const string BulkRetryRequested = "execution.operation_item.bulk_retry.requested";

    /// <summary>Historical only: nothing writes this action any more. Rows already written keep it (catalog entry marked retired).</summary>
    public const string BulkRetryCompleted = "execution.operation_item.bulk_retry.completed";
    public const string AttentionAcknowledged = "execution.operation_item.attention.acknowledged";
    public const string AttentionReopened = "execution.operation_item.attention.reopened";
}
