namespace Sample.ApiPolling.Contracts;

public enum SyncBatchStatus
{
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed,
}

/// <summary>Business change classification stored in operation item metadata (not platform Status).</summary>
public static class ApiPollingChangeKinds
{
    public const string New = "new";
    public const string Updated = "updated";
    public const string Unchanged = "unchanged";
    public const string Deleted = "deleted";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public static class ApiPollingRecordErrorCodes
{
    public const string MissingExternalId = "missing_external_id";
    public const string InvalidEmail = "invalid_email";
    public const string InvalidStatus = "invalid_status";
    public const string DuplicateExternalId = "duplicate_external_id";
    public const string InvalidUpdatedAt = "invalid_updated_at";
    public const string SkippedMaxRecords = "skipped_max_records";
    public const string SkippedFailOnRecordError = "skipped_fail_on_record_error";
}
