namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Durable platform store for operation groups/items (SDK-3A).
/// </summary>
public interface IOperationItemStore
{
    Task<OperationGroup> BeginGroupAsync(BeginOperationGroupRequest request, CancellationToken cancellationToken = default);

    Task<OperationItem> AppendItemAsync(AppendOperationItemRequest request, CancellationToken cancellationToken = default);

    Task<OperationGroup> CompleteGroupAsync(CompleteOperationGroupRequest request, CancellationToken cancellationToken = default);

    Task<OperationGroup?> GetGroupAsync(string operationGroupId, CancellationToken cancellationToken = default);

    Task<OperationItem?> GetItemAsync(string operationItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// SDK-4A — non-destructive counter bump only. Must not change Status, ErrorCode, or ErrorMessage.
    /// </summary>
    Task<OperationItem?> RecordRetryDispatchAsync(
        string operationItemId,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationItem>> GetItemsByGroupAsync(string operationGroupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationGroup>> GetGroupsByRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<OperationItemPage> QueryItemsByGroupAsync(
        string operationGroupId,
        OperationItemQuery query,
        CancellationToken cancellationToken = default);

    Task<OperationItemizationRunSummary> GetRunSummaryAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
