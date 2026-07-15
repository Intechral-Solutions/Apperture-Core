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

    Task<IReadOnlyList<OperationItem>> GetItemsByGroupAsync(string operationGroupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationGroup>> GetGroupsByRunAsync(string runId, CancellationToken cancellationToken = default);
}
