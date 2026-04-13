using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// SQL workflow start dispatch: persists a durable handoff (start request + run + instance) and returns a run id.
/// Does not execute workflow steps (ADR-0001). Definition resolution is lifecycle-governed (Epic 27 — Epic27-Execution-Surface-Inventory.md).
/// </summary>
public sealed class WorkflowStartDispatchService : IWorkflowStartDispatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly IWorkflowPersistence _persistence;
    private readonly AppertureDbContext _db;

    public WorkflowStartDispatchService(
        IWorkflowDefinitionResolver definitionResolver,
        IWorkflowPersistence persistence,
        AppertureDbContext db)
    {
        _definitionResolver = definitionResolver;
        _persistence = persistence;
        _db = db;
    }

    public async ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionResolver.GetByIdAsync(request.WorkflowDefinitionId, cancellationToken);
        if (definition is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.IntegrationId))
        {
            var integ = await _db.Integrations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.IntegrationId, cancellationToken);
            if (integ is null)
                throw new InvalidOperationException($"Integration '{request.IntegrationId}' was not found.");
            if (!string.Equals(integ.ModuleId, definition.ModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("The integration's module does not match this workflow's module.");
            if (integ.State != (int)Core.Domain.Integration.IntegrationState.Active)
                throw new InvalidOperationException($"Integration '{request.IntegrationId}' is not active.");
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.WorkflowStartRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return new WorkflowStartDispatchResult(
                    new OperationRunId(existing.RunId),
                    new WorkflowInstanceId(existing.WorkflowInstanceId),
                    new WorkflowDefinitionId(existing.WorkflowDefinitionId),
                    WorkflowLifecycleStatusNames.Requested);
            }
        }

        // EF Core SQL retry execution strategy does not allow user-initiated transactions
        // unless the whole unit of work is wrapped inside the execution strategy.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                var now = DateTimeOffset.UtcNow;
                var instanceId = new WorkflowInstanceId(NewId());
                var runId = new OperationRunId(NewId());

                // Persist atomically (instance + run + start request) to avoid orphaned state under failures.
                _db.WorkflowInstances.Add(new WorkflowInstanceEntity
                {
                    Id = instanceId.Value,
                    DefinitionId = request.WorkflowDefinitionId.Value,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CorrelationKey = request.CorrelationId,
                    IntegrationId = request.IntegrationId,
                    ProcedureId = request.ProcedureId,
                    CurrentStepName = null,
                    Version = 0
                });

                _db.OperationRuns.Add(new OperationRunEntity
                {
                    Id = runId.Value,
                    WorkflowInstanceId = instanceId.Value,
                    DefinitionId = request.WorkflowDefinitionId.Value,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    CreatedAt = now,
                    CompletedAt = null,
                    CorrelationKey = request.CorrelationId,
                    RetryOfRunId = request.RetryOfRunId,
                    IntegrationId = request.IntegrationId,
                    ProcedureId = request.ProcedureId,
                    ExecutionOrigin = request.ExecutionOrigin
                });

                _db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
                {
                    RunId = runId.Value,
                    WorkflowInstanceId = instanceId.Value,
                    WorkflowDefinitionId = request.WorkflowDefinitionId.Value,
                    ParametersJson = SerializeParameters(request.Parameters),
                    IdempotencyKey = request.IdempotencyKey,
                    CorrelationId = request.CorrelationId,
                    IntegrationId = request.IntegrationId,
                    ProcedureId = request.ProcedureId,
                    CreatedAt = now,
                    Status = WorkflowLifecycleStatusNames.Requested,
                    ClaimedAt = null,
                    ClaimedBy = null,
                    LastError = null
                });

                _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
                {
                    RunId = runId.Value,
                    WorkflowInstanceId = instanceId.Value,
                    WorkflowDefinitionId = request.WorkflowDefinitionId.Value,
                    EventType = WorkflowLifecycleEventNames.Dispatched,
                    FromStatus = null,
                    ToStatus = WorkflowLifecycleStatusNames.Requested,
                    OccurredAt = now,
                    RelatedRunId = request.RetryOfRunId,
                    StepName = null,
                    DetailsJson = null
                });

                await _db.SaveChangesAsync(cancellationToken);

                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);

                return new WorkflowStartDispatchResult(
                    runId,
                    instanceId,
                    request.WorkflowDefinitionId,
                    WorkflowLifecycleStatusNames.Requested);
            }
            catch (DbUpdateException ex) when (IsUniqueIdempotencyViolation(ex, request.IdempotencyKey))
            {
                // Another concurrent request won the race; return the existing run for this idempotency key.
                if (tx is not null)
                    await tx.RollbackAsync(cancellationToken);

                var existing = await _db.WorkflowStartRequests.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
                if (existing is null)
                    throw;

                return new WorkflowStartDispatchResult(
                    new OperationRunId(existing.RunId),
                    new WorkflowInstanceId(existing.WorkflowInstanceId),
                    new WorkflowDefinitionId(existing.WorkflowDefinitionId),
                    WorkflowLifecycleStatusNames.Requested);
            }
        });
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..24];

    /// <summary>
    /// Persists start parameters as a JSON object (ADR-0031). Not a stable key-order contract; same approach as <see cref="OperationStartDispatchService"/>.
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

        // SQL Server unique constraint violations:
        // 2601: Cannot insert duplicate key row in object with unique index
        // 2627: Violation of UNIQUE KEY constraint
        if (ex.InnerException is SqlException sql)
            return sql.Number is 2601 or 2627;

        return false;
    }
}
