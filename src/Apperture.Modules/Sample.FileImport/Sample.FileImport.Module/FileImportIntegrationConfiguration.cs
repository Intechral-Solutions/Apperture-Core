using Apperture.ModuleSdk.Integration.Configuration;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

internal static class FileImportIntegrationConfiguration
{
    internal static IReadOnlyList<IntegrationSettingDefinition> Settings { get; } =
    [
        StrictModeDefault(),
        MaxRowsPerImport(),
        FailOnFirstError(),
    ];

    internal static IReadOnlyList<ConnectionRequirementDefinition> Connections { get; } = [];

    internal static IReadOnlyList<ProcedureRequirementSet> Procedures { get; } = [];

    private static IntegrationSettingDefinition StrictModeDefault() =>
        new()
        {
            Key = FileImportSettingKeys.StrictModeDefault,
            DisplayName = "Strict mode default",
            Description =
                "When enabled and the procedure does not pass strictMode, any row failure finalizes the batch as Failed and emits import.failed.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = FileImportSettingDefaults.StrictModeDefault,
            Order = 1,
        };

    private static IntegrationSettingDefinition MaxRowsPerImport() =>
        new()
        {
            Key = FileImportSettingKeys.MaxRowsPerImport,
            DisplayName = "Max rows per import",
            Description =
                "Rows beyond this limit are Skipped with skipped_max_rows (not silently ignored). Overridable via maxRowsPerImport parameter.",
            ValueKind = IntegrationSettingValueKind.Integer,
            IsRequired = false,
            DefaultValue = FileImportSettingDefaults.MaxRowsPerImport,
            Order = 2,
        };

    private static IntegrationSettingDefinition FailOnFirstError() =>
        new()
        {
            Key = FileImportSettingKeys.FailOnFirstError,
            DisplayName = "Fail on first error",
            Description =
                "Stop after the first failed row; remaining known rows are Skipped with skipped_fail_on_first_error.",
            ValueKind = IntegrationSettingValueKind.Boolean,
            IsRequired = false,
            DefaultValue = FileImportSettingDefaults.FailOnFirstError,
            Order = 3,
        };
}
