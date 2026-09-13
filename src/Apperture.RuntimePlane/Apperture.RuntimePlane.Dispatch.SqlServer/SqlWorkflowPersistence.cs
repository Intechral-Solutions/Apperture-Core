using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.RuntimePlane.Dispatch.SqlServer;

/// <summary>
/// SQL-backed workflow persistence for the control plane start + run read surfaces.
/// Maps between RuntimePlane abstractions and Persistence.SqlServer entities.
/// </summary>
public sealed class SqlWorkflowPersistence : IWorkflowPersistence
{
    private readonly AppertureDbContext _db;

    public SqlWorkflowPersistence(AppertureDbContext db)
    {
        _db = db;
    }

    public async ValueTask<WorkflowInstance?> GetInstanceAsync(WorkflowInstanceId id, CancellationToken cancellationToken = default)
    {
        var e = await _db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async ValueTask SaveInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
    {
        var e = await _db.WorkflowInstances
            .FirstOrDefaultAsync(x => x.Id == instance.Id.Value, cancellationToken);

        if (e is null)
        {
            _db.WorkflowInstances.Add(ToEntity(instance));
        }
        else
        {
            e.DefinitionId = instance.DefinitionId.Value;
            e.Status = instance.Status;
            e.CreatedAt = instance.CreatedAt;
            e.UpdatedAt = instance.UpdatedAt;
            e.CorrelationKey = instance.CorrelationKey;
            e.CurrentStepName = instance.CurrentStepName;
            e.Version = instance.Version;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<OperationRun?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default)
    {
        var e = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async ValueTask SaveRunAsync(OperationRun run, CancellationToken cancellationToken = default)
    {
        var e = await _db.OperationRuns
            .FirstOrDefaultAsync(x => x.Id == run.Id.Value, cancellationToken);

        if (e is null)
        {
            _db.OperationRuns.Add(ToEntity(run));
        }
        else
        {
            e.WorkflowInstanceId = run.WorkflowInstanceId.Value;
            e.DefinitionId = run.DefinitionId.Value;
            e.Status = run.Status;
            e.CreatedAt = run.CreatedAt;
            e.CompletedAt = run.CompletedAt;
            e.CorrelationKey = run.CorrelationKey;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static WorkflowInstance ToDomain(WorkflowInstanceEntity e) => new(
        new WorkflowInstanceId(e.Id),
        new WorkflowDefinitionId(e.DefinitionId),
        e.Status,
        e.CreatedAt,
        e.UpdatedAt,
        e.CorrelationKey,
        e.CurrentStepName,
        e.Version);

    private static WorkflowInstanceEntity ToEntity(WorkflowInstance i) => new()
    {
        Id = i.Id.Value,
        DefinitionId = i.DefinitionId.Value,
        Status = i.Status,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
        CorrelationKey = i.CorrelationKey,
        CurrentStepName = i.CurrentStepName,
        Version = i.Version
    };

    private static OperationRun ToDomain(OperationRunEntity e) => new(
        new OperationRunId(e.Id),
        new WorkflowInstanceId(e.WorkflowInstanceId),
        new WorkflowDefinitionId(e.DefinitionId),
        e.Status,
        e.CreatedAt,
        e.CompletedAt,
        e.CorrelationKey,
        e.RetryOfRunId);

    private static OperationRunEntity ToEntity(OperationRun r) => new()
    {
        Id = r.Id.Value,
        WorkflowInstanceId = r.WorkflowInstanceId.Value,
        DefinitionId = r.DefinitionId.Value,
        Status = r.Status,
        CreatedAt = r.CreatedAt,
        CompletedAt = r.CompletedAt,
        CorrelationKey = r.CorrelationKey,
        RetryOfRunId = r.RetryOfRunId
    };
}
