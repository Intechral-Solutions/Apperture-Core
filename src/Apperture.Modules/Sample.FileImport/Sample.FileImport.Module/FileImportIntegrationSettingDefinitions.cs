using Apperture.Core.Application.PlatformSettings;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

internal static class FileImportIntegrationSettingDefinitions
{
    private static readonly PlatformSettingValueScopeKind[] IntegrationScopes =
        [PlatformSettingValueScopeKind.Integration];

    internal static IReadOnlyList<PlatformSettingDefinition> All { get; } =
    [
        StrictModeDefault(),
        MaxRowsPerImport(),
        FailOnFirstError(),
    ];

    private static PlatformSettingDefinition StrictModeDefault() =>
        new()
        {
            Key = FileImportSettingKeys.StrictModeDefault,
            DisplayName = "Strict mode default",
            Description =
                "When enabled and the procedure does not pass strictMode, any row failure finalizes the batch as Failed and emits import.failed.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.FileImport:Import:StrictModeDefault",
            DefaultValue = FileImportSettingDefaults.StrictModeDefault,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition MaxRowsPerImport() =>
        new()
        {
            Key = FileImportSettingKeys.MaxRowsPerImport,
            DisplayName = "Max rows per import",
            Description =
                "Rows beyond this limit are Skipped with skipped_max_rows (not silently ignored). Overridable via maxRowsPerImport parameter.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Integer,
            InputKind = PlatformSettingInputKind.Number,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.FileImport:Import:MaxRowsPerImport",
            DefaultValue = FileImportSettingDefaults.MaxRowsPerImport,
            RequiresRestart = false,
        };

    private static PlatformSettingDefinition FailOnFirstError() =>
        new()
        {
            Key = FileImportSettingKeys.FailOnFirstError,
            DisplayName = "Fail on first error",
            Description =
                "Stop after the first failed row; remaining known rows are Skipped with skipped_fail_on_first_error.",
            Category = PlatformSettingCategory.Demo,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            AllowedValueScopes = IntegrationScopes,
            ConfigurationPath = "Modules:Sample.FileImport:Import:FailOnFirstError",
            DefaultValue = FileImportSettingDefaults.FailOnFirstError,
            RequiresRestart = false,
        };
}
