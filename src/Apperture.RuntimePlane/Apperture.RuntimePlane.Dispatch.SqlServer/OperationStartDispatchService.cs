using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// SQL operation start dispatch: persists a durable handoff (operation instance + run + start request) and returns a run id.
/// Operation resolution is lifecycle-governed (Epic 27 — Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class OperationStartDispatchService : IOperationStartDispatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOperationRegistryService _operations;
    private readonly AppertureDbContext _db;

    public OperationStartDispatchService(
        IOperationRegistryService operations,
        AppertureDbContext db)
    {
        _operations = operations;
        _db = db;
    }

    public async ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default)
    {
        var operationId = new OperationId(request.OperationDefinitionId);
        var definition = await _operations.GetOperationAsync(operationId, cancellationToken);
        if (definition is null)
            return null;

        // CTX-2: resolve the occurrence's ResourceContext once, here, before any row is written.
        // Never re-derived later from current ownership. A caller-supplied value (e.g. the firing
        // Schedule's context) wins; otherwise derive from the Integration already being validated
        // below — no extra query. Contextless (no Integration, no supplied value) stays null.
        var resourceContextId = request.ResourceContextId;

        if (!string.IsNullOrWhiteSpace(request.IntegrationId))
        {
            var integ = await _db.Integrations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.IntegrationId, cancellationToken);
            if (integ is null)
                throw new InvalidOperationException($"Integration '{request.IntegrationId}' was not found.");
            if (!string.Equals(integ.ModuleId, definition.ModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("The integration's module does not match this operation's module.");
            if (integ.State != (int)Core.Domain.Integration.IntegrationState.Active)
                throw new InvalidOperationException($"Integration '{request.IntegrationId}' is not active.");

            resourceContextId ??= integ.ResourceContextId;
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.OperationStartRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (existing is not null)
                return Replay(existing, operationId);
        }

        // AUD-1.3 (design gate §13.4): inside an audited unit of work the start joins the unit's transaction, so the start
        // request and its audit row commit together, and the unit owns the commit, verification and any rerun. A failed insert
        // is rolled back to the savepoint EF takes around SaveChanges, which leaves the unit's transaction usable.
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is not null)
            return await PersistJoinedAsync(request, operationId, resourceContextId, cancellationToken);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                var rows = AddRows(request, operationId, resourceContextId);
                await _db.SaveChangesAsync(cancellationToken);

                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);

                return rows.Result;
            }
            catch (DbUpdateException ex) when (IsUniqueIdempotencyViolation(ex, request.IdempotencyKey))
            {
                if (tx is not null)
                    await tx.RollbackAsync(cancellationToken);

                var existing = await _db.OperationStartRequests.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                if (existing is null)
                    throw;

                return Replay(existing, operationId);
            }
        });
    }

    private async ValueTask<OperationStartDispatchResult?> PersistJoinedAsync(
        StartOperationRequest request,
        OperationId operationId,
        string? resourceContextId,
        CancellationToken cancellationToken)
    {
        var rows = AddRows(request, operationId, resourceContextId);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return rows.Result;
        }
        catch (DbUpdateException ex) when (IsUniqueIdempotencyViolation(ex, request.IdempotencyKey))
        {
            // Another concurrent request won the race. Our rows were rolled back to the savepoint; stop tracking them, which
            // leaves the unit's other pending changes alone, and report the earlier run for this idempotency key.
            foreach (var row in rows.Entities)
                _db.Entry(row).State = EntityState.Detached;

            var existing = await _db.OperationStartRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is null)
                throw;

            return Replay(existing, operationId);
        }
    }

    private static OperationStartDispatchResult Replay(OperationStartRequestEntity existing, OperationId operationId) =>
        new(
            new OperationRunId(existing.RunId),
            new OperationInstanceId(existing.OperationInstanceId),
            operationId,
            WorkflowLifecycleStatusNames.Requested,
            IsReplay: true,
            ResourceContextId: existing.ResourceContextId);

    /// <summary>
    /// Adds the instance, the run, the start request and the first lifecycle event (persisted atomically, to avoid orphaned
    /// state on failures). Nothing is saved here.
    /// </summary>
    private (OperationStartDispatchResult Result, IReadOnlyList<object> Entities) AddRows(
        StartOperationRequest request,
        OperationId operationId,
        string? resourceContextId)
    {
        var now = DateTimeOffset.UtcNow;
        var instanceId = new OperationInstanceId(NewId());
        var runId = new OperationRunId(NewId());

        var instance = new OperationInstanceEntity
        {
            Id = instanceId.Value,
            DefinitionId = operationId.Value,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = now,
            UpdatedAt = now,
            CorrelationKey = request.CorrelationId,
            CurrentStepName = null,
            Version = 0,
            ResourceContextId = resourceContextId
        };
        var run = new OperationRunEntity
        {
            Id = runId.Value,
            WorkflowInstanceId = instanceId.Value,
            DefinitionId = operationId.Value,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = now,
            CompletedAt = null,
            CorrelationKey = request.CorrelationId,
            RetryOfRunId = null,
            IntegrationId = request.IntegrationId,
            ProcedureId = request.ProcedureId,
            ExecutionOrigin = request.ExecutionOrigin,
            ResourceContextId = resourceContextId
        };
        var startRequest = new OperationStartRequestEntity
        {
            RunId = runId.Value,
            OperationInstanceId = instanceId.Value,
            OperationDefinitionId = operationId.Value,
            ParametersJson = SerializeParameters(request.Parameters),
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = request.CorrelationId,
            IntegrationId = request.IntegrationId,
            ProcedureId = request.ProcedureId,
            CreatedAt = now,
            RequestedBy = request.RequestedBy,
            Status = WorkflowLifecycleStatusNames.Requested,
            ClaimedAt = null,
            ClaimedBy = null,
            LastError = null,
            ResourceContextId = resourceContextId
        };
        var lifecycle = new OperationLifecycleEventEntity
        {
            RunId = runId.Value,
            OperationInstanceId = instanceId.Value,
            OperationDefinitionId = operationId.Value,
            EventType = OperationLifecycleEventNames.Dispatched,
            FromStatus = null,
            ToStatus = WorkflowLifecycleStatusNames.Requested,
            OccurredAt = now,
            RelatedRunId = null,
            StepName = null,
            DetailsJson = null
        };

        _db.OperationInstances.Add(instance);
        _db.OperationRuns.Add(run);
        _db.OperationStartRequests.Add(startRequest);
        _db.OperationLifecycleEvents.Add(lifecycle);

        return (
            new OperationStartDispatchResult(
                runId,
                instanceId,
                operationId,
                WorkflowLifecycleStatusNames.Requested,
                IsReplay: false,
                ResourceContextId: resourceContextId),
            [instance, run, startRequest, lifecycle]);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..24];

    /// <summary>
    /// Persists start parameters as a JSON object (ADR-0031). Not a stable key-order contract; same approach as <see cref="WorkflowStartDispatchService"/>.
    /// </summary>
    private static string? SerializeParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return null;

        return JsonSerializer.Serialize(parameters, JsonOptions);
    }

    private static bool IsUniqueIdempotencyViolation(DbUpdateException ex, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return false;

        if (ex.InnerException is SqlException sql)
            return sql.Number is 2601 or 2627;

        return false;
    }
}
