namespace Sample.ApiPolling.Contracts;

public static class ApiPollingSettingKeys
{
    public const string PageSize = "modules.sample.apipolling.pageSize";
    public const string MaxRecordsPerSync = "modules.sample.apipolling.maxRecordsPerSync";
    public const string IncludeDeleted = "modules.sample.apipolling.includeDeleted";
    public const string FailOnRecordError = "modules.sample.apipolling.failOnRecordError";
    public const string StrictModeDefault = "modules.sample.apipolling.strictModeDefault";
}

public static class ApiPollingSettingDefaults
{
    public const int PageSize = 50;
    public const int MaxRecordsPerSync = 1000;
    public const bool IncludeDeleted = true;
    public const bool FailOnRecordError = false;
    public const bool StrictModeDefault = false;
}
