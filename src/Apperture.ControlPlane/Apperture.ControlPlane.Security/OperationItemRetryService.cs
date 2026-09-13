using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Lists retry attempts for an item under a run. Null when run/item missing.</summary>
    Task<IReadOnlyList<OperationItemRetryAttemptDto>?> ListAttemptsAsync(
        OperationRunId runId,
        string operationItemId,
        CancellationToken cancellationToken = default);
}

public sealed record RequestOperationItemRetryDto(
    string IdempotencyKey,
    string? Reason = null);

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

public sealed class OperationItemRetryService : IOperationItemRetryService
{
    private readonly AppertureDbContext _db;
    private readonly IOperationItemStore _items;
    private readonly IOperationItemRetryAttemptStore _attempts;
    private readonly IOperationItemRetryReconciliationService _reconcile;
    private readonly IEnumerable<IOperationItemRetryHandler> _handlers;
    private readonly IWorkflowRunRetryAuthorizationService _retryAuthz;
    private readonly IWorkflowStartDispatchService _dispatch;
    private readonly IPrincipalContext _principal;

    public OperationItemRetryService(
        AppertureDbContext db,
        IOperationItemStore items,
        IOperationItemRetryAttemptStore attempts,
        IOperationItemRetryReconciliationService reconcile,
        IEnumerable<IOperationItemRetryHandler> handlers,
        IWorkflowRunRetryAuthorizationService retryAuthz,
        IWorkflowStartDispatchService dispatch,
        IPrincipalContext principal)
    {
        _db = db;
        _items = items;
        _attempts = attempts;
        _reconcile = reconcile;
        _handlers = handlers;
        _retryAuthz = retryAuthz;
        _dispatch = dispatch;
        _principal = principal;
    }

    public async Task<OperationItemRetryAttemptDto?> RequestRetryAsync(
        OperationRunId runId,
        string operationItemId,
        RequestOperationItemRetryDto request,
        CancellationToken cancellationToken = default)
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

        var existing = await _attempts.GetByIdempotencyKeyAsync(item.OperationItemId, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var reconciled = await _reconcile.ReconcileAttemptAsync(existing.OperationItemRetryAttemptId, cancellationToken)
                .ConfigureAwait(false) ?? existing;
            return Map(reconciled, GuidanceFor(reconciled, idempotentReplay: true));
        }

        var group = await _items.GetGroupAsync(item.OperationGroupId, cancellationToken).ConfigureAwait(false);
        if (group is null)
            return null;

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

        var actor = await _principal.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var actorId = actor?.Id.Value;
        var attemptIndex = await _attempts.GetNextAttemptIndexAsync(item.OperationItemId, cancellationToken)
            .ConfigureAwait(false);

        var eligibility = handler.Evaluate(item, group);
        if (!eligibility.CanRetry)
        {
            var blocked = await _attempts.CreateAsync(
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

            // Concurrent create may have returned the winning attempt for this key.
            if (!string.Equals(blocked.Status, OperationItemRetryAttemptStatuses.Blocked, StringComparison.Ordinal)
                && string.Equals(blocked.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            {
                return Map(blocked, "Idempotent replay of an existing retry attempt.");
            }

            return Map(blocked, eligibility.BlockReasonMessage ?? "Retry blocked; child run was not dispatched.");
        }

        var requested = await _attempts.CreateAsync(
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

        if (!string.Equals(requested.Status, OperationItemRetryAttemptStatuses.Requested, StringComparison.Ordinal))
            return Map(requested, "Idempotent replay of an existing retry attempt.");

        OperationItemRetryStartPlan plan;
        try
        {
            plan = handler.BuildStartPlan(new OperationItemRetryBuildContext(
                item,
                group,
                requested.OperationItemRetryAttemptId,
                idempotencyKey,
                runId.Value));
        }
        catch (OperationItemizationException ex)
        {
            var blocked = await _attempts.MarkBlockedAsync(
                new MarkOperationItemRetryAttemptBlockedRequest(
                    requested.OperationItemRetryAttemptId,
                    OperationItemRetryErrorCodes.MissingRetryMetadata,
                    ex.Message),
                cancellationToken).ConfigureAwait(false);
            return Map(blocked, ex.Message);
        }

        var dispatchKey = BuildDispatchIdempotencyKey(item.OperationItemId, idempotencyKey);
        var dispatchResult = await _dispatch.StartAsync(
            new StartWorkflowRequest(
                plan.WorkflowDefinitionId,
                plan.Parameters,
                dispatchKey,
                CorrelationId: $"opitem-retry:{item.OperationItemId}:{requested.AttemptIndex}",
                RetryOfRunId: runId.Value,
                IntegrationId: plan.IntegrationId ?? group.IntegrationId,
                ProcedureId: plan.ProcedureId ?? group.ProcedureId,
                ExecutionOrigin: RunExecutionOriginNames.Manual),
            cancellationToken).ConfigureAwait(false);

        if (dispatchResult is null)
        {
            var blocked = await _attempts.MarkBlockedAsync(
                new MarkOperationItemRetryAttemptBlockedRequest(
                    requested.OperationItemRetryAttemptId,
                    OperationItemRetryErrorCodes.DefinitionUnavailable,
                    "Workflow definition is not executable (module disabled or unavailable)."),
                cancellationToken).ConfigureAwait(false);
            return Map(blocked, "Retry blocked; child run was not dispatched.");
        }

        var dispatched = await _attempts.MarkDispatchedAsync(
            new MarkOperationItemRetryAttemptDispatchedRequest(
                requested.OperationItemRetryAttemptId,
                dispatchResult.RunId.Value),
            cancellationToken).ConfigureAwait(false);

        await _items.RecordRetryDispatchAsync(item.OperationItemId, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Map(
            dispatched,
            GuidanceFor(dispatched, idempotentReplay: false));
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
