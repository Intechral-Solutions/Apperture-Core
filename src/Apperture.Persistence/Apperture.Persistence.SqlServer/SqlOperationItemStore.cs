using Apperture.Core.Application.OperationItems;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlOperationItemStore : IOperationItemStore
{
    private readonly AppertureDbContext _db;

    public SqlOperationItemStore(AppertureDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<OperationGroup> BeginGroupAsync(
        BeginOperationGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        OperationItemizationValidation.ValidateBegin(request);

        var now = request.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var entity = new OperationGroupEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            RunId = request.RunId.Trim(),
            IntegrationId = request.IntegrationId,
            ModuleId = request.ModuleId.Trim(),
            ProcedureId = request.ProcedureId,
            OperationKind = request.OperationKind.Trim(),
            SourceLabel = request.SourceLabel,
            Status = OperationGroupStatuses.Running,
            TotalCount = 0,
            SucceededCount = 0,
            FailedCount = 0,
            SkippedCount = 0,
            StartedAtUtc = now,
            CompletedAtUtc = null,
            SummaryJson = request.SummaryJson,
            MetadataJson = request.MetadataJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.OperationGroups.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapGroup(entity);
    }

    public async Task<OperationItem> AppendItemAsync(
        AppendOperationItemRequest request,
        CancellationToken cancellationToken = default)
    {
        OperationItemizationValidation.ValidateAppend(request);

        var group = await _db.OperationGroups
            .FirstOrDefaultAsync(g => g.Id == request.OperationGroupId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationItemizationException($"Unknown operation group '{request.OperationGroupId}'.");

        if (group.Status != OperationGroupStatuses.Running)
            throw new OperationItemizationException($"Cannot append to group '{request.OperationGroupId}' in status '{group.Status}'.");

        if (!string.Equals(group.RunId, request.RunId, StringComparison.Ordinal))
            throw new OperationItemizationException("AppendItem RunId does not match the group's RunId.");

        var now = DateTimeOffset.UtcNow;
        var entity = new OperationItemEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationGroupId = request.OperationGroupId,
            RunId = request.RunId.Trim(),
            Sequence = request.Sequence,
            SubjectKey = request.SubjectKey,
            DisplayLabel = request.DisplayLabel,
            Status = request.Status,
            ErrorCode = request.ErrorCode,
            ErrorMessage = request.ErrorMessage,
            IsRetryable = request.IsRetryable,
            RetryCount = request.RetryCount,
            LastAttemptedAtUtc = request.LastAttemptedAtUtc,
            CompletedAtUtc = request.CompletedAtUtc
                             ?? (request.Status == OperationItemStatuses.Pending ? null : now),
            ChangeSummaryJson = request.ChangeSummaryJson,
            MetadataJson = request.MetadataJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.OperationItems.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapItem(entity);
    }

    public async Task<OperationGroup> CompleteGroupAsync(
        CompleteOperationGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        OperationItemizationValidation.ValidateComplete(request);

        var group = await _db.OperationGroups
            .FirstOrDefaultAsync(g => g.Id == request.OperationGroupId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationItemizationException($"Unknown operation group '{request.OperationGroupId}'.");

        if (group.Status != OperationGroupStatuses.Running)
            throw new OperationItemizationException($"Group '{request.OperationGroupId}' is already completed.");

        var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        group.Status = request.Status;
        group.TotalCount = request.TotalCount;
        group.SucceededCount = request.SucceededCount;
        group.FailedCount = request.FailedCount;
        group.SkippedCount = request.SkippedCount;
        group.SummaryJson = request.SummaryJson ?? group.SummaryJson;
        group.CompletedAtUtc = now;
        group.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapGroup(group);
    }

    public async Task<OperationGroup?> GetGroupAsync(string operationGroupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return null;

        var entity = await _db.OperationGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == operationGroupId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MapGroup(entity);
    }

    public async Task<IReadOnlyList<OperationItem>> GetItemsByGroupAsync(
        string operationGroupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationGroupId))
            return [];

        var items = await _db.OperationItems
            .AsNoTracking()
            .Where(i => i.OperationGroupId == operationGroupId)
            .OrderBy(i => i.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items.Select(MapItem).ToArray();
    }

    public async Task<IReadOnlyList<OperationGroup>> GetGroupsByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return [];

        var groups = await _db.OperationGroups
            .AsNoTracking()
            .Where(g => g.RunId == runId)
            .OrderBy(g => g.StartedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups.Select(MapGroup).ToArray();
    }

    private static OperationGroup MapGroup(OperationGroupEntity e) =>
        new(
            e.Id,
            e.RunId,
            e.IntegrationId,
            e.ModuleId,
            e.ProcedureId,
            e.OperationKind,
            e.SourceLabel,
            e.Status,
            e.TotalCount,
            e.SucceededCount,
            e.FailedCount,
            e.SkippedCount,
            e.StartedAtUtc,
            e.CompletedAtUtc,
            e.SummaryJson,
            e.MetadataJson,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);

    private static OperationItem MapItem(OperationItemEntity e) =>
        new(
            e.Id,
            e.OperationGroupId,
            e.RunId,
            e.Sequence,
            e.SubjectKey,
            e.DisplayLabel,
            e.Status,
            e.ErrorCode,
            e.ErrorMessage,
            e.IsRetryable,
            e.RetryCount,
            e.LastAttemptedAtUtc,
            e.CompletedAtUtc,
            e.ChangeSummaryJson,
            e.MetadataJson,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
}
