namespace Apperture.Core.Application.OperationItems;

/// <summary>JOB-1 — durable store for operation-item attention acknowledgements.</summary>
public interface IOperationItemAttentionAcknowledgementStore
{
    Task<OperationItemAttentionAcknowledgement> CreateAsync(
        CreateOperationItemAttentionAcknowledgementRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationItemAttentionAcknowledgement?> GetActiveByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, OperationItemAttentionAcknowledgement>> GetActiveByItemIdsAsync(
        IReadOnlyCollection<string> operationItemIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationItemAttentionAcknowledgement>> ListByItemIdAsync(
        string operationItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens the active acknowledgement (sets ReopenedAtUtc). Returns null when none active.
    /// Prior rows are retained; history is append-only via reopen metadata on the same row.
    /// </summary>
    Task<OperationItemAttentionAcknowledgement?> ReopenActiveAsync(
        string operationItemId,
        string reopenedByUserId,
        DateTimeOffset? reopenedAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAcknowledgedFailuresByRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAcknowledgedFailuresByGroupAsync(
        string operationGroupId,
        CancellationToken cancellationToken = default);
}
