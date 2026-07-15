namespace Sample.FileImport.Contracts;

public enum ImportBatchStatus
{
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed,
}

public enum ImportRowStatus
{
    Succeeded,
    Failed,
    Skipped,
}

public static class ImportRowErrorCodes
{
    public const string MissingExternalId = "missing_external_id";
    public const string InvalidEmail = "invalid_email";
    public const string InvalidStatus = "invalid_status";
    public const string DuplicateExternalId = "duplicate_external_id";
    public const string InvalidDate = "invalid_date";
    public const string ParseError = "parse_error";
    public const string SkippedMaxRows = "skipped_max_rows";
    public const string SkippedFailOnFirstError = "skipped_fail_on_first_error";
}
