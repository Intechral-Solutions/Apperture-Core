namespace Apperture.Core.Application.OperationItems;

/// <summary>SDK-4A — statuses for durable per-item retry attempts.</summary>
public static class OperationItemRetryAttemptStatuses
{
    public const string Requested = "Requested";
    public const string Dispatched = "Dispatched";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Blocked = "Blocked";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Requested,
        Dispatched,
        Succeeded,
        Failed,
        Blocked,
    };
}

public static class OperationItemRetryErrorCodes
{
    public const string MissingRetryMetadata = "missing_retry_metadata";
    public const string RowNotFound = "retry_row_not_found";
    public const string RecordNotFound = "retry_record_not_found";
    public const string NotFailed = "item_not_failed";
    public const string NotRetryable = "item_not_retryable";
    public const string ModuleUnsupported = "module_retry_unsupported";
    public const string DefinitionUnavailable = "definition_not_executable";
    public const string IntegrationNotRunnable = "integration_not_runnable";

    // SDK-4B reconciliation
    public const string ChildRunCancelled = "child_run_cancelled";
    public const string ChildRunFailedBeforeItemization = "child_run_failed_before_itemization";
    public const string MissingChildOutcome = "missing_child_outcome";
    public const string AmbiguousChildOutcome = "ambiguous_child_outcome";
    public const string ChildItemSkipped = "child_item_skipped";
    public const string ChildRunMissing = "child_run_missing";
}

/// <summary>Size bounds for retry attempt fields (SDK-4A).</summary>
public static class OperationItemRetryBounds
{
    public const int IdempotencyKeyMaxLength = 128;
    public const int ReasonMaxLength = 512;
    public const int ActorIdMaxLength = 128;
    public const int DispatchIdempotencyKeyMaxLength = 128;
}
