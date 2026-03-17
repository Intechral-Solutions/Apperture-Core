using System.Text.Json;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Control-plane workflow start dispatch: persists a durable handoff (start request + run + instance) and returns a run id.
/// Does not execute workflow steps (ADR-0001).
/// </summary>
public sealed class WorkflowStartDispatchService : IWorkflowStartDispatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string RequestedStatus = "requested";

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
                    RequestedStatus);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var instanceId = new WorkflowInstanceId(NewId());
        var runId = new OperationRunId(NewId());

        var instance = new WorkflowInstance(
            instanceId,
            request.WorkflowDefinitionId,
            RequestedStatus,
            now,
            now,
            request.CorrelationId,
            CurrentStepName: null,
            Version: 0);

        var run = new OperationRun(
            runId,
            instanceId,
            request.WorkflowDefinitionId,
            RequestedStatus,
            now,
            CompletedAt: null,
            request.CorrelationId);

        await _persistence.SaveInstanceAsync(instance, cancellationToken);
        await _persistence.SaveRunAsync(run, cancellationToken);

        _db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
        {
            RunId = runId.Value,
            WorkflowInstanceId = instanceId.Value,
            WorkflowDefinitionId = request.WorkflowDefinitionId.Value,
            ParametersJson = SerializeParameters(request.Parameters),
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = request.CorrelationId,
            CreatedAt = now,
            Status = RequestedStatus,
            ClaimedAt = null,
            ClaimedBy = null,
            LastError = null
        });
        await _db.SaveChangesAsync(cancellationToken);

        return new WorkflowStartDispatchResult(runId, instanceId, request.WorkflowDefinitionId, RequestedStatus);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..24];

    private static string? SerializeParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return null;
        return JsonSerializer.Serialize(parameters, JsonOptions);
    }
}

