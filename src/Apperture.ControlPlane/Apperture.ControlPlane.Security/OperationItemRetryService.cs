using System.Security.Cryptography;
using System.Text;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public interface IOperationItemRetryService
{
    /// <summary>
    /// Requests a per-item retry. Returns null when the run or item is missing / mismatched.
    /// Throws <see cref="UnauthorizedAccessException"/> when denied.
    /// Throws <see cref="OperationItemRetryException"/> for eligibility / validation conflicts.
    /// </summary>
    Task<OperationItemRetryAttemptDto?> RequestRetryAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="RequestRetryAsync(OperationRunId, string, RequestOperationItemRetryDto, CancellationToken)"/>, for one item
    /// of a bulk retry: the item's own audit row names the batch it belongs to.
    /// </summary>
    Task<OperationItemRetryAttemptDto?> RequestRetryAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        OperationItemRetryBatch batch,
        CancellationToken cancellationToken = default);

    /// <summary>Lists retry attempts for an item under a run. Null when run/item missing.</summary>
    Task<IReadOnlyList<OperationItemRetryAttemptDto>?> ListAttemptsAsync(
        OperationRunId runId,
        string operationItemId,
        CancellationToken cancellationToken = default);
}

public sealed record RequestOperationItemRetryDto(
    string IdempotencyKey,
    string? Reason = null);

/// <summary>The bulk retry an item retry belongs to. Set by the bulk retry service, never taken from a request body.</summary>
public sealed record OperationItemRetryBatch(string RequestId);

public sealed record OperationItemRetryAttemptDto(
    string OperationItemRetryAttemptId,
    string OperationItemId,
    string RunId,
    string OperationGroupId,
    int AttemptIndex,
    string IdempotencyKey,
    string? RequestedByActorId,
    string? Reason,
    string Status,
    string? ChildRunId,
    string? ChildOperationGroupId,
    string? ChildOperationItemId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Message = null);

/// <summary>Eligibility / validation failure for per-item retry (maps to 400/409).</summary>
public sealed class OperationItemRetryException : InvalidOperationException
{
    public OperationItemRetryException(string errorCode, string message, bool isConflict = true)
        : base(message)
    {
        ErrorCode = errorCode;
        IsConflict = isConflict;
    }

    public string ErrorCode { get; }

    /// <summary>When true, API should return 409; otherwise 400.</summary>
    public bool IsConflict { get; }
}

/// <summary>
/// Requests a retry of one failed operation item (design gate §13.1 W1, §22 AUD-1.3). The retry attempt, the child run it starts
/// (with its start request, which names the requester), the item's retry stamp and the audit row commit in one audited unit of
/// work. Before this, the attempt, the dispatch and the dispatched mark were separate commits, so a failure between them left an
/// attempt that said "requested" with nothing started. The attempt records the unit's own actor as
/// <c>RequestedByActorId</c>, so the attempt row and the audit row share the actor by construction.
/// </summary>
public sealed class OperationItemRetryService : IOperationItemRetryService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemRetryAttemptStore _attempts;
    private readonly IOperationItemRetryReconciliationService _reconcile;
    private readonly IEnumerable<IOperationItemRetryHandler> _handlers;
    private readonly IWorkflowRunRetryAuthorizationService _retryAuthz;
    private readonly IWorkflowStartDispatchService _dispatch;
    private readonly IAuditedUnitOfWork _unit;

    public OperationItemRetryService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        IOperationItemRetryReconciliationService reconcile,
        IEnumerable<IOperationItemRetryHandler> handlers,
        IWorkflowRunRetryAuthorizationService retryAuthz,
        IWorkflowStartDispatchService dispatch,
        IAuditedUnitOfWork unit)
    {
        _db = db;
        _items = items;
        _attempts = attempts;
        _reconcile = reconcile;
        _handlers = handlers;
        _retryAuthz = retryAuthz;
        _dispatch = dispatch;
        _unit = unit;
    }

    public Task<OperationItemRetryAttemptDto?> RequestRetryAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        CancellationToken cancellationToken = default) =>
        RequestRetryCoreAsync(runId, operationItemId, request, batch: null, cancellationToken);

    public Task<OperationItemRetryAttemptDto?> RequestRetryAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        OperationItemRetryBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return RequestRetryCoreAsync(runId, operationItemId, request, batch, cancellationToken);
    }

    private async Task<OperationItemRetryAttemptDto?> RequestRetryCoreAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        OperationItemRetryBatch? batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(operationItemId))
            return null;

        var idempotencyKey = (request.IdempotencyKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new OperationItemRetryException("idempotency_key_required", "idempotencyKey is required.", isConflict: false);
        if (idempotencyKey.Length > OperationItemRetryBounds.IdempotencyKeyMaxLength)
            throw new OperationItemRetryException(
                "idempotency_key_too_long",
                $"idempotencyKey exceeds {OperationItemRetryBounds.IdempotencyKeyMaxLength} characters.",
                isConflict: false);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > OperationItemRetryBounds.ReasonMaxLength })
            throw new OperationItemRetryException(
                "reason_too_long",
                $"reason exceeds {OperationItemRetryBounds.ReasonMaxLength} characters.",
                isConflict: false);

        var run = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
            return null;

        await _retryAuthz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken)
            .ConfigureAwait(false);

        var item = await _items.GetItemAsync(operationItemId.Trim(), cancellationToken).ConfigureAwait(false);
        if (item is null)
            return null;
        if (!string.Equals(item.RunId, runId.Value, StringComparison.Ordinal))
            return null;

        // A replay of a request this key already made changes nothing, so it is answered outside the unit: reconciling the
        // earlier attempt's outcome is a write of its own that a no-op unit would roll back.
        var existing = await _attempts.GetByIdempotencyKeyAsync(item.OperationItemId, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var reconciled = await _reconcile.ReconcileAttemptAsync(existing.OperationItemRetryAttemptId, cancellationToken)
                .ConfigureAwait(false) ?? existing;
            return Map(reconciled, GuidanceFor(reconciled, idempotentReplay: true));
        }

        return await _unit.ExecuteAsync(
            new AuditedOperation(
                AuditActionNames.OperationItemRetryRequested,
                AuditActor.CurrentPrincipal,
                AlternatePrimaryActions: [AuditActionNames.OperationItemRetryBlocked]),
            (transaction, ct) => AttemptAsync(transaction, run, item.OperationItemId, idempotencyKey, reason, batch?.RequestId, ct),
            ReconstructAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One attempt of the unit: every read the decision depends on is repeated here, inside the transaction, and the attempt,
    /// the child run and the audit row are written together or not at all.
    /// </summary>
    private async Task<OperationItemRetryAttemptDto?> AttemptAsync(
        IAuditedTransaction transaction,
        OperationRunEntity run,
        string operationItemId,
        string idempotencyKey,
        string? reason,
        string? bulkRequestId,
        CancellationToken cancellationToken)
    {
        var item = await _items.GetItemAsync(operationItemId, cancellationToken).ConfigureAwait(false);
        if (item is null || !string.Equals(item.RunId, run.Id, StringComparison.Ordinal))
        {
            transaction.DeclareNoOp();
            return null;
        }

        var existing = await _attempts.GetByIdempotencyKeyAsync(item.OperationItemId, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // A concurrent request with this key committed first.
            transaction.DeclareNoOp();
            return Map(existing, GuidanceFor(existing, idempotentReplay: true));
        }

        var group = await _items.GetGroupAsync(item.OperationGroupId, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            transaction.DeclareNoOp();
            return null;
        }

        if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
        {
            throw new OperationItemRetryException(
                OperationItemRetryErrorCodes.NotFailed,
                $"Operation item '{item.OperationItemId}' is not Failed (status '{item.Status}').");
        }

        if (!item.IsRetryable)
        {
            throw new OperationItemRetryException(
                OperationItemRetryErrorCodes.NotRetryable,
                $"Operation item '{item.OperationItemId}' is not marked retryable.");
        }

        var handler = _handlers.FirstOrDefault(h => h.Supports(item, group));
        if (handler is null)
        {
            throw new OperationItemRetryException(
                OperationItemRetryErrorCodes.ModuleUnsupported,
                $"No retry handler is registered for module '{group.ModuleId}' / operation kind '{group.OperationKind}'.");
        }

        var actorId = transaction.Actor.RecordedValue;
        var attemptIndex = await _attempts.GetNextAttemptIndexAsync(item.OperationItemId, cancellationToken)
            .ConfigureAwait(false);

        var eligibility = handler.Evaluate(item, group);
        if (!eligibility.CanRetry)
        {
            var blocked = await _attempts.TryCreateAsync(
                new CreateOperationItemRetryAttemptRequest(
                    OperationItemId: item.OperationItemId,
                    RunId: item.RunId,
                    OperationGroupId: item.OperationGroupId,
                    AttemptIndex: attemptIndex,
                    IdempotencyKey: idempotencyKey,
                    RequestedByActorId: actorId,
                    Reason: reason,
                    Status: OperationItemRetryAttemptStatuses.Blocked,
                    ErrorCode: eligibility.BlockReasonCode ?? OperationItemRetryErrorCodes.MissingRetryMetadata,
                    ErrorMessage: eligibility.BlockReasonMessage ?? "Retry blocked."),
                cancellationToken).ConfigureAwait(false);
            if (blocked is null)
                return await LostToConcurrentRequestAsync(transaction, item.OperationItemId, idempotencyKey, cancellationToken).ConfigureAwait(false);

            await StageBlockedAsync(transaction, run, blocked, bulkRequestId, cancellationToken).ConfigureAwait(false);
            return Map(blocked, eligibility.BlockReasonMessage ?? "Retry blocked; child run was not dispatched.");
        }

        var requested = await _attempts.TryCreateAsync(
            new CreateOperationItemRetryAttemptRequest(
                OperationItemId: item.OperationItemId,
                RunId: item.RunId,
                OperationGroupId: item.OperationGroupId,
                AttemptIndex: attemptIndex,
                IdempotencyKey: idempotencyKey,
                RequestedByActorId: actorId,
                Reason: reason,
                Status: OperationItemRetryAttemptStatuses.Requested),
            cancellationToken).ConfigureAwait(false);
        if (requested is null)
            return await LostToConcurrentRequestAsync(transaction, item.OperationItemId, idempotencyKey, cancellationToken).ConfigureAwait(false);

        OperationItemRetryStartPlan plan;
        try
        {
            plan = handler.BuildStartPlan(new OperationItemRetryBuildContext(
                item,
                group,
                requested.OperationItemRetryAttemptId,
                idempotencyKey,
                run.Id));
        }
        catch (OperationItemizationException ex)
        {
            var blocked = await _attempts.MarkBlockedAsync(
                new MarkOperationItemRetryAttemptBlockedRequest(
                    requested.OperationItemRetryAttemptId,
                    OperationItemRetryErrorCodes.MissingRetryMetadata,
                    ex.Message),
                cancellationToken).ConfigureAwait(false);
            await StageBlockedAsync(transaction, run, blocked, bulkRequestId, cancellationToken).ConfigureAwait(false);
            return Map(blocked, ex.Message);
        }

        var dispatchKey = BuildDispatchIdempotencyKey(item.OperationItemId, idempotencyKey);
        var dispatchResult = await _dispatch.StartAsync(
            new StartWorkflowRequest(
                plan.WorkflowDefinitionId,
                plan.Parameters,
                dispatchKey,
                CorrelationId: $"opitem-retry:{item.OperationItemId}:{requested.AttemptIndex}",
                RetryOfRunId: run.Id,
                IntegrationId: plan.IntegrationId ?? group.IntegrationId,
                ProcedureId: plan.ProcedureId ?? group.ProcedureId,
                ExecutionOrigin: RunExecutionOriginNames.Manual,
                RequestedBy: actorId),
            cancellationToken).ConfigureAwait(false);

        if (dispatchResult is null)
        {
            var blocked = await _attempts.MarkBlockedAsync(
                new MarkOperationItemRetryAttemptBlockedRequest(
                    requested.OperationItemRetryAttemptId,
                    OperationItemRetryErrorCodes.DefinitionUnavailable,
                    "Workflow definition is not executable (module disabled or unavailable)."),
                cancellationToken).ConfigureAwait(false);
            await StageBlockedAsync(transaction, run, blocked, bulkRequestId, cancellationToken).ConfigureAwait(false);
            return Map(blocked, "Retry blocked; child run was not dispatched.");
        }

        var dispatched = await _attempts.MarkDispatchedAsync(
            new MarkOperationItemRetryAttemptDispatchedRequest(
                requested.OperationItemRetryAttemptId,
                dispatchResult.RunId.Value),
            cancellationToken).ConfigureAwait(false);

        await _items.RecordRetryDispatchAsync(item.OperationItemId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        await transaction.StageAsync(
            AuditEvents.OperationItemRetryRequested(
                item.OperationItemId,
                run.Id,
                item.OperationGroupId,
                dispatched.OperationItemRetryAttemptId,
                dispatched.AttemptIndex,
                dispatchResult.RunId.Value,
                reason,
                run.ResourceContextId,
                bulkRequestId),
            cancellationToken).ConfigureAwait(false);

        return Map(
            dispatched,
            GuidanceFor(dispatched, idempotentReplay: false));
    }

    /// <summary>
    /// A concurrent request with the same key committed its attempt first, so this one created nothing. It reports that
    /// attempt as the replay it is and records nothing: the attempt, and its row, belong to the request that made it.
    /// </summary>
    private async Task<OperationItemRetryAttemptDto> LostToConcurrentRequestAsync(
        IAuditedTransaction transaction,
        string operationItemId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        transaction.DeclareNoOp();
        var winner = await _attempts.GetByIdempotencyKeyAsync(operationItemId, idempotencyKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A retry attempt that won the idempotency key could not be read.");
        return Map(winner, GuidanceFor(winner, idempotentReplay: true));
    }

    private static ValueTask StageBlockedAsync(
        IAuditedTransaction transaction,
        OperationRunEntity run,
        OperationItemRetryAttempt blocked,
        string? bulkRequestId,
        CancellationToken cancellationToken) =>
        transaction.StageAsync(
            AuditEvents.OperationItemRetryBlocked(
                blocked.OperationItemId,
                run.Id,
                blocked.OperationGroupId,
                blocked.OperationItemRetryAttemptId,
                blocked.AttemptIndex,
                blocked.ErrorCode ?? OperationItemRetryErrorCodes.MissingRetryMetadata,
                blocked.Reason,
                run.ResourceContextId,
                bulkRequestId),
            cancellationToken);

    /// <summary>
    /// The attempt a confirmed commit created, from its primary row: read back for its current state, or built from the
    /// recorded fields where it can no longer be read.
    /// </summary>
    private async Task<OperationItemRetryAttemptDto?> ReconstructAsync(AuditCommittedOperation committed, CancellationToken cancellationToken)
    {
        var attemptId = committed.GetField("attemptId") ?? throw new InvalidOperationException("The committed retry records no attempt.");
        var attempt = await _attempts.GetByIdAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is not null)
            return Map(attempt, GuidanceFor(attempt, idempotentReplay: false));

        // Attempt rows are never deleted, so this is a guard, not an expected path: the commit is still reported as the
        // success it was, from what its audit row recorded.
        var now = DateTimeOffset.UtcNow;
        var dispatched = string.Equals(committed.Action, AuditActionNames.OperationItemRetryRequested, StringComparison.Ordinal);
        return new OperationItemRetryAttemptDto(
            attemptId,
            committed.TargetId ?? "",
            committed.GetField("runId") ?? "",
            committed.GetField("operationGroupId") ?? "",
            AttemptIndex: 0,
            IdempotencyKey: "",
            RequestedByActorId: null,
            committed.GetField("reason"),
            dispatched ? OperationItemRetryAttemptStatuses.Dispatched : OperationItemRetryAttemptStatuses.Blocked,
            committed.GetField("childRunId"),
            ChildOperationGroupId: null,
            ChildOperationItemId: null,
            now,
            CompletedAtUtc: null,
            committed.GetField("errorCode"),
            ErrorMessage: null,
            now,
            now);
    }
    public async Task<IReadOnlyList<OperationItemRetryAttemptDto>?> ListAttemptsAsync(
        OperationRunId runId,
        string operationItemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationItemId))
            return null;

        var run = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
            return null;

        // Execute-equivalent authz (same as retry request) — not read-only.
        await _retryAuthz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(run.DefinitionId), cancellationToken)
            .ConfigureAwait(false);

        var item = await _items.GetItemAsync(operationItemId.Trim(), cancellationToken).ConfigureAwait(false);
        if (item is null || !string.Equals(item.RunId, runId.Value, StringComparison.Ordinal))
            return null;

        var attempts = await _reconcile.ReconcileForParentItemAsync(item.OperationItemId, cancellationToken)
            .ConfigureAwait(false);
        return attempts.Select(a => Map(a, GuidanceFor(a, idempotentReplay: false))).ToArray();
    }

    public static string BuildDispatchIdempotencyKey(string operationItemId, string clientIdempotencyKey)
    {
        var raw = $"opitem-retry:{operationItemId}:{clientIdempotencyKey}";
        if (raw.Length <= OperationItemRetryBounds.DispatchIdempotencyKeyMaxLength)
            return raw;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"oir:{hash[..48]}";
    }

    private static string GuidanceFor(OperationItemRetryAttempt a, bool idempotentReplay)
    {
        var prefix = idempotentReplay ? "Idempotent replay. " : "";
        return a.Status switch
        {
            OperationItemRetryAttemptStatuses.Dispatched =>
                prefix + "Retry attempt dispatched; waiting for child run outcome.",
            OperationItemRetryAttemptStatuses.Succeeded =>
                prefix + "Retry attempt succeeded (child operation item succeeded).",
            OperationItemRetryAttemptStatuses.Failed =>
                prefix + "Retry attempt failed.",
            OperationItemRetryAttemptStatuses.Blocked =>
                prefix + "Retry attempt blocked; child run was not dispatched or could not proceed.",
            OperationItemRetryAttemptStatuses.Requested =>
                prefix + "Retry attempt requested.",
            _ => prefix.TrimEnd(),
        };
    }

    private static OperationItemRetryAttemptDto Map(OperationItemRetryAttempt a, string? message) =>
        new(
            a.OperationItemRetryAttemptId,
            a.OperationItemId,
            a.RunId,
            a.OperationGroupId,
            a.AttemptIndex,
            a.IdempotencyKey,
            a.RequestedByActorId,
            a.Reason,
            a.Status,
            a.ChildRunId,
            a.ChildOperationGroupId,
            a.ChildOperationItemId,
            a.StartedAtUtc,
            a.CompletedAtUtc,
            a.ErrorCode,
            a.ErrorMessage,
            a.CreatedAtUtc,
            a.UpdatedAtUtc,
            message);
}
