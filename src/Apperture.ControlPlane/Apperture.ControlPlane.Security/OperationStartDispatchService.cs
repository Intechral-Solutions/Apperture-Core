using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Control-plane operation start dispatch: persists a durable handoff (operation instance + run + start request) and returns a run id.
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

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.OperationStartRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return new OperationStartDispatchResult(
                    new OperationRunId(existing.RunId),
                    new OperationInstanceId(existing.OperationInstanceId),
                    operationId,
                    WorkflowLifecycleStatusNames.Requested);
            }
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                var now = DateTimeOffset.UtcNow;
                var instanceId = new OperationInstanceId(NewId());
                var runId = new OperationRunId(NewId());

                // Persist atomically (instance + run + start request) to avoid orphaned state on failures.
                _db.OperationInstances.Add(new OperationInstanceEntity
                {
                    Id = instanceId.Value,
                    DefinitionId = operationId.Value,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CorrelationKey = request.CorrelationId,
                    CurrentStepName = null,
                    Version = 0
                });

                _db.OperationRuns.Add(new OperationRunEntity
                {
                    Id = runId.Value,
                    WorkflowInstanceId = instanceId.Value,
                    DefinitionId = operationId.Value,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    CreatedAt = now,
                    CompletedAt = null,
                    CorrelationKey = request.CorrelationId,
                    RetryOfRunId = null
                });

                _db.OperationStartRequests.Add(new OperationStartRequestEntity
                {
                    RunId = runId.Value,
                    OperationInstanceId = instanceId.Value,
                    OperationDefinitionId = operationId.Value,
                    ParametersJson = SerializeParameters(request.Parameters),
                    IdempotencyKey = request.IdempotencyKey,
                    CorrelationId = request.CorrelationId,
                    CreatedAt = now,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    ClaimedAt = null,
                    ClaimedBy = null,
                    LastError = null
                });

                _db.OperationLifecycleEvents.Add(new OperationLifecycleEventEntity
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
                });

                await _db.SaveChangesAsync(cancellationToken);

                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);

                return new OperationStartDispatchResult(
                    runId,
                    instanceId,
                    operationId,
                    WorkflowLifecycleStatusNames.Requested);
            }
            catch (DbUpdateException ex) when (IsUniqueIdempotencyViolation(ex, request.IdempotencyKey))
            {
                if (tx is not null)
                    await tx.RollbackAsync(cancellationToken);

                var existing = await _db.OperationStartRequests.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                if (existing is null)
                    throw;

                return new OperationStartDispatchResult(
                    new OperationRunId(existing.RunId),
                    new OperationInstanceId(existing.OperationInstanceId),
                    operationId,
                    WorkflowLifecycleStatusNames.Requested);
            }
        });
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..24];

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

