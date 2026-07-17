using Apperture.Core.Application.OperationItems;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlOperationItemRetryAttemptStore : IOperationItemRetryAttemptStore
{
    private readonly AppertureDbContext _db;

    public SqlOperationItemRetryAttemptStore(AppertureDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<OperationItemRetryAttempt> CreateAsync(
        CreateOperationItemRetryAttemptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreate(request);

        var now = request.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var entity = new OperationItemRetryAttemptEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationItemId = request.OperationItemId.Trim(),
            RunId = request.RunId.Trim(),
            OperationGroupId = request.OperationGroupId.Trim(),
            AttemptIndex = request.AttemptIndex,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            RequestedByActorId = Truncate(request.RequestedByActorId, OperationItemRetryBounds.ActorIdMaxLength),
            Reason = Truncate(request.Reason, OperationItemRetryBounds.ReasonMaxLength),
            Status = request.Status.Trim(),
            ChildRunId = request.ChildRunId,
            ChildOperationGroupId = request.ChildOperationGroupId,
            ChildOperationItemId = request.ChildOperationItemId,
            StartedAtUtc = now,
            CompletedAtUtc = request.Status is OperationItemRetryAttemptStatuses.Blocked
                or OperationItemRetryAttemptStatuses.Failed
                or OperationItemRetryAttemptStatuses.Succeeded
                ? now
                : null,
            ErrorCode = Truncate(request.ErrorCode, OperationItemizationBounds.ErrorCodeMaxLength),
            ErrorMessage = Truncate(request.ErrorMessage, OperationItemizationBounds.ErrorMessageMaxLength),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.OperationItemRetryAttempts.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(entity).State = EntityState.Detached;
            var existing = await GetByIdempotencyKeyAsync(request.OperationItemId, request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
                return existing;
            throw;
        }

        return Map(entity);
    }

    public async Task<OperationItemRetryAttempt?> GetByIdAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
            return null;

        var entity = await _db.OperationItemRetryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<OperationItemRetryAttempt?> GetByIdempotencyKeyAsync(
        string operationItemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId) || string.IsNullOrWhiteSpace(idempotencyKey))
            return null;

        var entity = await _db.OperationItemRetryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.OperationItemId == operationItemId && a.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<OperationItemRetryAttempt>> ListByItemAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return [];

        var rows = await _db.OperationItemRetryAttempts
            .AsNoTracking()
            .Where(a => a.OperationItemId == operationItemId)
            .OrderBy(a => a.AttemptIndex)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToArray();
    }

    public async Task<int> GetNextAttemptIndexAsync(
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return 1;

        var max = await _db.OperationItemRetryAttempts
            .AsNoTracking()
            .Where(a => a.OperationItemId == operationItemId)
            .Select(a => (int?)a.AttemptIndex)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return (max ?? 0) + 1;
    }

    public async Task<OperationItemRetryAttempt> MarkDispatchedAsync(
        MarkOperationItemRetryAttemptDispatchedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationItemRetryAttemptId))
            throw new OperationItemizationException("Retry attempt id is required.");
        if (string.IsNullOrWhiteSpace(request.ChildRunId))
            throw new OperationItemizationException("Child run id is required when marking dispatched.");

        var entity = await _db.OperationItemRetryAttempts
            .FirstOrDefaultAsync(a => a.Id == request.OperationItemRetryAttemptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

        var now = request.DispatchedAtUtc ?? DateTimeOffset.UtcNow;
        entity.Status = OperationItemRetryAttemptStatuses.Dispatched;
        entity.ChildRunId = request.ChildRunId.Trim();
        entity.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<OperationItemRetryAttempt> MarkBlockedAsync(
        MarkOperationItemRetryAttemptBlockedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationItemRetryAttemptId))
            throw new OperationItemizationException("Retry attempt id is required.");

        var entity = await _db.OperationItemRetryAttempts
            .FirstOrDefaultAsync(a => a.Id == request.OperationItemRetryAttemptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

        var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        entity.Status = OperationItemRetryAttemptStatuses.Blocked;
        entity.ErrorCode = Truncate(request.ErrorCode, OperationItemizationBounds.ErrorCodeMaxLength);
        entity.ErrorMessage = Truncate(request.ErrorMessage, OperationItemizationBounds.ErrorMessageMaxLength);
        entity.CompletedAtUtc = now;
        entity.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<OperationItemRetryAttempt?> GetByChildRunIdAsync(
        string childRunId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(childRunId))
            return null;

        var entity = await _db.OperationItemRetryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ChildRunId == childRunId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<OperationItemRetryAttempt> MarkTerminalAsync(
        MarkOperationItemRetryAttemptTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationItemRetryAttemptId))
            throw new OperationItemizationException("Retry attempt id is required.");

        var status = request.Status.Trim();
        if (status is not (OperationItemRetryAttemptStatuses.Succeeded
            or OperationItemRetryAttemptStatuses.Failed
            or OperationItemRetryAttemptStatuses.Blocked))
        {
            throw new OperationItemizationException(
                $"Terminal status must be Succeeded, Failed, or Blocked (got '{request.Status}').");
        }

        var entity = await _db.OperationItemRetryAttempts
            .FirstOrDefaultAsync(a => a.Id == request.OperationItemRetryAttemptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OperationItemizationException($"Unknown retry attempt '{request.OperationItemRetryAttemptId}'.");

        // Already terminal: keep existing outcome (do not overwrite Succeeded with Failed, etc.).
        if (entity.Status is OperationItemRetryAttemptStatuses.Succeeded
            or OperationItemRetryAttemptStatuses.Failed
            or OperationItemRetryAttemptStatuses.Blocked)
        {
            return Map(entity);
        }

        var now = request.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        entity.Status = status;
        entity.ChildOperationGroupId = request.ChildOperationGroupId ?? entity.ChildOperationGroupId;
        entity.ChildOperationItemId = request.ChildOperationItemId ?? entity.ChildOperationItemId;
        entity.ErrorCode = Truncate(request.ErrorCode, OperationItemizationBounds.ErrorCodeMaxLength);
        entity.ErrorMessage = Truncate(request.ErrorMessage, OperationItemizationBounds.ErrorMessageMaxLength);
        entity.CompletedAtUtc = now;
        entity.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    private static void ValidateCreate(CreateOperationItemRetryAttemptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OperationItemId))
            throw new OperationItemizationException("OperationItemId is required.");
        if (string.IsNullOrWhiteSpace(request.RunId))
            throw new OperationItemizationException("RunId is required.");
        if (string.IsNullOrWhiteSpace(request.OperationGroupId))
            throw new OperationItemizationException("OperationGroupId is required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new OperationItemizationException("IdempotencyKey is required.");
        if (request.IdempotencyKey.Trim().Length > OperationItemRetryBounds.IdempotencyKeyMaxLength)
            throw new OperationItemizationException("IdempotencyKey exceeds max length.");
        if (request.AttemptIndex < 1)
            throw new OperationItemizationException("AttemptIndex must be >= 1.");
        if (!OperationItemRetryAttemptStatuses.All.Contains(request.Status))
            throw new OperationItemizationException($"Unknown retry attempt status '{request.Status}'.");
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
                return true;
            if (e.Message.Contains("IX_OperationItemRetryAttempts_OperationItemId_IdempotencyKey", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("IX_OperationItemRetryAttempts_OperationItemId_AttemptIndex", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : (value.Length <= max ? value : value[..max]);

    private static OperationItemRetryAttempt Map(OperationItemRetryAttemptEntity e) =>
        new(
            e.Id,
            e.OperationItemId,
            e.RunId,
            e.OperationGroupId,
            e.AttemptIndex,
            e.IdempotencyKey,
            e.RequestedByActorId,
            e.Reason,
            e.Status,
            e.ChildRunId,
            e.ChildOperationGroupId,
            e.ChildOperationItemId,
            e.StartedAtUtc,
            e.CompletedAtUtc,
            e.ErrorCode,
            e.ErrorMessage,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
}
