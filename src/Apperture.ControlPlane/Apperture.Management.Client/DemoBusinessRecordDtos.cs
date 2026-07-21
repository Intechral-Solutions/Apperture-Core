namespace Apperture.Management.Client;

/// <summary>
/// Demo/sample customer business-record projection (BDS-3/BDS-4).
/// Not a platform-wide <c>BusinessRecord</c> entity model.
/// </summary>
public sealed record DemoCustomerRecordDto(
    string RecordType,
    string BusinessKey,
    string? DisplayName,
    string? PrimaryEmail,
    string? PrimaryStatus,
    int SourceCount,
    bool HasConflict,
    int ConflictCount,
    DateTimeOffset? LastObservedAtUtc,
    IReadOnlyList<DemoCustomerRecordSourceDto> Sources,
    IReadOnlyList<DemoCustomerRecordConflictDto> Conflicts,
    string? PrimaryConflictSummary = null);

public sealed record DemoCustomerRecordSourceDto(
    string SourceKind,
    string SourceKindLabel,
    string ModuleId,
    string? IntegrationId,
    string? OperationKind,
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    string BusinessKey,
    string? DisplayName,
    string? Email,
    string? Status,
    string? ChangeKind,
    DateTimeOffset ObservedAtUtc,
    string ProvenanceLabel);

public sealed record DemoCustomerRecordConflictDto(
    string Field,
    IReadOnlyDictionary<string, string> ValuesBySource,
    string Message);

public sealed record DemoCustomerRecordPageDto(
    IReadOnlyList<DemoCustomerRecordDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record DemoCustomerRecordListFilter(
    string? Q = null,
    string? SourceKind = null,
    bool? HasConflict = null,
    int Skip = 0,
    int Take = 50);
