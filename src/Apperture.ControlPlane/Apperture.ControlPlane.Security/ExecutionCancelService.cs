using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Run-level cancel adapter that routes to the correct cancel service based on persisted run kind.
/// </summary>
public sealed class ExecutionCancelService : IExecutionCancelService
{
    private readonly AppertureDbContext _db;
    private readonly IWorkflowCancelService _workflowCancel;
    private readonly IOperationCancelService _operationCancel;

    public ExecutionCancelService(
        AppertureDbContext db,
        IWorkflowCancelService workflowCancel,
        IOperationCancelService operationCancel)
    {
        _db = db;
        _workflowCancel = workflowCancel;
        _operationCancel = operationCancel;
    }

    public async ValueTask<bool> RequestCancelAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        // Identify run kind by which instance record exists.
        var run = await _db.OperationRuns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (run is null)
            return false;

        var hasWorkflowInstance = await _db.WorkflowInstances.AsNoTracking()
            .AnyAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);
        if (hasWorkflowInstance)
            return await _workflowCancel.RequestCancelAsync(runId, cancellationToken);

        var hasOperationInstance = await _db.OperationInstances.AsNoTracking()
            .AnyAsync(x => x.Id == run.WorkflowInstanceId, cancellationToken);
        if (hasOperationInstance)
            return await _operationCancel.RequestCancelAsync(runId, cancellationToken);

        return false;
    }
}

