using System.Text.Json;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class WorkflowRetryService : IWorkflowRetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppertureDbContext _db;
    private readonly IWorkflowRunRetryAuthorizationService _authz;
    private readonly IWorkflowStartDispatchService _dispatch;

    public WorkflowRetryService(
        AppertureDbContext db,
        IWorkflowRunRetryAuthorizationService authz,
        IWorkflowStartDispatchService dispatch)
    {
        _db = db;
        _authz = authz;
        _dispatch = dispatch;
    }

    public async ValueTask<WorkflowRetryResult?> RequestRetryAsync(OperationRunId runId, CancellationToken cancellationToken = default)
    {
        var originalRun = await _db.OperationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId.Value, cancellationToken);
        if (originalRun is null)
            return null;

        if (!IsRetryableStatus(originalRun.Status))
            throw new InvalidOperationException($"Run {runId.Value} is not eligible for retry from status '{originalRun.Status}'.");

        var sourceRequest = await _db.WorkflowStartRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId.Value, cancellationToken);
        if (sourceRequest is null)
            throw new InvalidOperationException($"Original start context for run {runId.Value} could not be recovered.");

        await _authz.EnsureCanRetryRunAsync(new WorkflowDefinitionId(originalRun.DefinitionId), cancellationToken);

        var parameters = DeserializeParameters(sourceRequest.ParametersJson);
        var dispatchResult = await _dispatch.StartAsync(
            new StartWorkflowRequest(
                new WorkflowDefinitionId(originalRun.DefinitionId),
                parameters,
                null,
                sourceRequest.CorrelationId,
                runId.Value),
            cancellationToken);

        if (dispatchResult is null)
            throw new InvalidOperationException($"Workflow definition {originalRun.DefinitionId} could not be resolved for retry.");

        _db.WorkflowLifecycleEvents.Add(new WorkflowLifecycleEventEntity
        {
            RunId = originalRun.Id,
            WorkflowInstanceId = originalRun.WorkflowInstanceId,
            WorkflowDefinitionId = originalRun.DefinitionId,
            EventType = WorkflowLifecycleEventNames.RetryRequested,
            FromStatus = originalRun.Status,
            ToStatus = originalRun.Status,
            OccurredAt = DateTimeOffset.UtcNow,
            RelatedRunId = dispatchResult.RunId.Value,
            StepName = null,
            DetailsJson = null
        });
        await _db.SaveChangesAsync(cancellationToken);

        return new WorkflowRetryResult(
            new OperationRunId(originalRun.Id),
            dispatchResult.RunId,
            dispatchResult.WorkflowInstanceId,
            dispatchResult.WorkflowDefinitionId,
            dispatchResult.Status);
    }

    private static bool IsRetryableStatus(string status) =>
        string.Equals(status, WorkflowLifecycleStatusNames.Failed, StringComparison.Ordinal) ||
        string.Equals(status, WorkflowLifecycleStatusNames.Cancelled, StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, object?>? DeserializeParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson, JsonOptions);
        if (raw is null || raw.Count == 0)
            return null;

        return raw.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
    }
}

