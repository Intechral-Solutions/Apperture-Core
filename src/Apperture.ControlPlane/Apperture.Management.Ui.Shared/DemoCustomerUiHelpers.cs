using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>BDS-5 — display helpers for the Demo Customers surface.</summary>
public static class DemoCustomerUiHelpers
{
    public const int DefaultPageSize = 50;

    public const string DemoBadge = "Demo";

    public const string PageTitle = "Customers";

    public const string PageSubtitle =
        "Customer records correlated across import and API polling sources.";

    public const string DemoScopeBanner =
        "This collection uses sandbox sample data. It is not a production business-record store.";

    public const string EmptyTitle = "No customers yet";

    public const string EmptyMessage =
        "No customer records have been projected yet. Run Sample.FileImport and/or Sample.ApiPolling, then refresh.";

    public const string EmptySearchTitle = "No matching customers";

    public const string EmptySearchMessage =
        "No customers match the current search or filters.";

    public const string EmptyConflictsTitle = "No conflicts";

    public const string EmptyConflictsMessage =
        "No demo customers currently have soft conflicts across FileImport and ApiPolling.";

    public const string ForbiddenMessage =
        "You do not have permission to view customer records (requires integration read).";

    public const string NotFoundMessage =
        "That customer was not found. It may not have been observed yet, or the business key is incorrect.";

    public const string LoadErrorMessage =
        "Could not load customer records.";

    public const string SourceKindAll = "";
    public const string SourceKindFileImport = "file_import";
    public const string SourceKindApiPolling = "api_polling";

    public static IReadOnlyList<(string Value, string Label)> SourceKindOptions { get; } =
    [
        (SourceKindAll, "All sources"),
        (SourceKindFileImport, "File import"),
        (SourceKindApiPolling, "API polling"),
    ];

    public static DemoCustomerRecordListFilter BuildListFilter(
        string? query,
        string? sourceKind,
        bool conflictsOnly,
        int skip,
        int take = DefaultPageSize) =>
        new(
            Q: string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            SourceKind: string.IsNullOrWhiteSpace(sourceKind) ? null : sourceKind.Trim(),
            HasConflict: conflictsOnly ? true : null,
            Skip: Math.Max(0, skip),
            Take: take <= 0 ? DefaultPageSize : take);

    public static bool FiltersActive(string? query, string? sourceKind, bool conflictsOnly) =>
        !string.IsNullOrWhiteSpace(query)
        || !string.IsNullOrWhiteSpace(sourceKind)
        || conflictsOnly;

    public static (string Title, string Message) ResolveEmptyCopy(
        string? query,
        string? sourceKind,
        bool conflictsOnly)
    {
        if (conflictsOnly && string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(sourceKind))
            return (EmptyConflictsTitle, EmptyConflictsMessage);
        if (FiltersActive(query, sourceKind, conflictsOnly))
            return (EmptySearchTitle, EmptySearchMessage);
        return (EmptyTitle, EmptyMessage);
    }

    public static string CustomerDisplayName(DemoCustomerRecordDto record) =>
        string.IsNullOrWhiteSpace(record.DisplayName) ? record.BusinessKey : record.DisplayName.Trim();

    public static string FormatSourceCount(int sourceCount) =>
        sourceCount == 1 ? "1 source" : $"{sourceCount} sources";

    public static ManagementStatusSemantic ConflictSemantic(bool hasConflict) =>
        hasConflict ? ManagementStatusSemantic.Warning : ManagementStatusSemantic.Success;

    public static string ConflictLabel(DemoCustomerRecordDto record) =>
        record.HasConflict
            ? (string.IsNullOrWhiteSpace(record.PrimaryConflictSummary)
                ? $"{record.ConflictCount} conflict(s)"
                : record.PrimaryConflictSummary!)
            : "No conflicts";

    public static string GetRunDetailHref(string runId) =>
        ProcedureLaunchOperatorUiHelpers.GetRunDetailHref(runId);

    public static string? TryGetIntegrationHref(string? integrationId) =>
        string.IsNullOrWhiteSpace(integrationId)
            ? null
            : IntegrationUiRoutes.Detail(integrationId);

    public static string FormatFieldLabel(string? field) =>
        field?.Trim().ToLowerInvariant() switch
        {
            "email" => "Email",
            "status" => "Status",
            "displayname" => "Display name",
            _ => string.IsNullOrWhiteSpace(field) ? "Field" : field.Trim(),
        };
}
