using Apperture.ModuleSdk.Integration.Configuration;
using Sample.ApiPolling.Contracts;

namespace Sample.ApiPolling;

internal static class ApiPollingIntegrationConfiguration
{
    internal static IReadOnlyList<IntegrationSettingDefinition> Settings { get; } =
    [
        PageSize(),
        MaxRecordsPerSync(),
        IncludeDeleted(),
        FailOnRecordError(),
        StrictModeDefault(),
    ];

    internal static IReadOnlyList<ConnectionRequirementDefinition> Connections { get; } =
    [
        DirectoryApi(),
    ];

    internal static IReadOnlyList<ProcedureRequirementSet> Procedures { get; } =
    [
        new ProcedureRequirementSet
        {
            ProcedureId = "Sample.ApiPolling.procedures.poll",
            RequiredConnectionKeys = [ApiPollingConnectionKeys.DirectoryApi],
        },
    ];

    private static ConnectionRequirementDefinition DirectoryApi() =>
        new()
        {
            Key = ApiPollingConnectionKeys.DirectoryApi,
            DisplayName = "Directory API",
            Description =
                "Sample directory API client used to poll external account snapshots.",
            IsRequired = true,
            CapabilityContractType = typeof(ISampleDirectoryApiClient).FullName!,
            CapabilityKind = ConnectionCapabilityKinds.HttpApi,
            VerificationExpectation = ConnectionVerificationExpectation.UnsupportedOk,
            Order = 1,
        };

    private static IntegrationSettingDefinition PageSize() =>
        new()
        {
            Key = ApiPollingSettingKeys.PageSize,
            DisplayName = "Page size",
            Description = "API page size when polling directory snapshots.",
            ValueKind = IntegrationSettingValueKind.Integer,
            IsRequired = false,
            DefaultValue = ApiPollingSettingDefaults.PageSize,
            Order = 1,
        };

    private static IntegrationSettingDefinition MaxRecordsPerSync() =>
        new()
        {
            Key = ApiPollingSettingKeys.MaxRecordsPerSync,
            DisplayName = "Max records per sync",
            Description =
                "Records beyond this limit are Skipped with skipped_max_records (not silently ignored).",
            ValueKind = IntegrationSettingValueKind.Integer,
            IsRequired = false,
            DefaultValue = ApiPollingSettingDefaults.MaxRecordsPerSync,
            Order = 2,
        };

    private static IntegrationSettingDefinition IncludeDeleted() =>
        new()
        {
            Key = ApiPollingSettingKeys.IncludeDeleted,
            DisplayName = "Include deleted",
            Description =
                "When enabled, tombstoned records and records missing from the latest snapshot are classified as deleted.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = ApiPollingSettingDefaults.IncludeDeleted,
            Order = 3,
        };

    private static IntegrationSettingDefinition FailOnRecordError() =>
        new()
        {
            Key = ApiPollingSettingKeys.FailOnRecordError,
            DisplayName = "Fail on record error",
            Description =
                "Stop after the first failed record; remaining known records are Skipped with skipped_fail_on_record_error.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = ApiPollingSettingDefaults.FailOnRecordError,
            Order = 4,
        };

    private static IntegrationSettingDefinition StrictModeDefault() =>
        new()
        {
            Key = ApiPollingSettingKeys.StrictModeDefault,
            DisplayName = "Strict mode default",
            Description =
                "When enabled and the procedure does not pass strictMode, any record failure finalizes the sync as Failed and emits sync.failed.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = ApiPollingSettingDefaults.StrictModeDefault,
            Order = 5,
        };
}
