using Apperture.Core.Application.PlatformSettings;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

internal static class ApiPollingIntegrationSettingDefinitions
{
    private static readonly PlatformSettingValueScopeKind[] IntegrationScopes =
        [PlatformSettingValueScopeKind.Integration];

    internal static IReadOnlyList<PlatformSettingDefinition> All { get; } =
    [
        PageSize(),
        MaxRecordsPerSync(),
        IncludeDeleted(),
        FailOnRecordError(),
        StrictModeDefault(),
    ];

    private static PlatformSettingDefinition PageSize() =>
        new()
        {
            Key = ApiPollingSettingKeys.PageSize,
            DisplayName = "Page size",
            Description =
                "Fake API page size when polling embedded snapshots. Does not require network access.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Integer,
            InputKind = PlatformSettingInputKind.Number,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.ApiPolling:Poll:PageSize",
            DefaultValue = ApiPollingSettingDefaults.PageSize,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition MaxRecordsPerSync() =>
        new()
        {
            Key = ApiPollingSettingKeys.MaxRecordsPerSync,
            DisplayName = "Max records per sync",
            Description =
                "Records beyond this limit are Skipped with skipped_max_records (not silently ignored).",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Integer,
            InputKind = PlatformSettingInputKind.Number,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.ApiPolling:Poll:MaxRecordsPerSync",
            DefaultValue = ApiPollingSettingDefaults.MaxRecordsPerSync,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition IncludeDeleted() =>
        new()
        {
            Key = ApiPollingSettingKeys.IncludeDeleted,
            DisplayName = "Include deleted",
            Description =
                "When enabled, tombstoned records and records missing from the latest snapshot are classified as deleted.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.ApiPolling:Poll:IncludeDeleted",
            DefaultValue = ApiPollingSettingDefaults.IncludeDeleted,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition FailOnRecordError() =>
        new()
        {
            Key = ApiPollingSettingKeys.FailOnRecordError,
            DisplayName = "Fail on record error",
            Description =
                "Stop after the first failed record; remaining known records are Skipped with skipped_fail_on_record_error.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.ApiPolling:Poll:FailOnRecordError",
            DefaultValue = ApiPollingSettingDefaults.FailOnRecordError,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition StrictModeDefault() =>
        new()
        {
            Key = ApiPollingSettingKeys.StrictModeDefault,
            DisplayName = "Strict mode default",
            Description =
                "When enabled and the procedure does not pass strictMode, any record failure finalizes the sync as Failed and emits sync.failed.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.ApiPolling:Poll:StrictModeDefault",
            DefaultValue = ApiPollingSettingDefaults.StrictModeDefault,
            RequiresRestart = false,
        };
}
