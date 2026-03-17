using System.Text.Json;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Apperture.Persistence.SqlServer.Options;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Processes persisted workflow start requests (claim + execute + lifecycle updates).
/// Intended for instance-plane worker hosts.
/// </summary>
public sealed class WorkflowStartRequestProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string RequestedStatus = "requested";
    private const string RunningStatus = "running";
    private const string CompletedStatus = "completed";
    private const string FailedStatus = "failed";

    private readonly AppertureDbContext _db;
    private readonly IWorkflowExecutor _executor;
    private readonly IWorkflowDefinitionResolver _definitionResolver;
    private readonly ILogger? _logger;
    private readonly string _workerId;
    private readonly WorkflowStartRequestLeaseOptions _leaseOptions;

    public WorkflowStartRequestProcessor(
        AppertureDbContext db,
        IWorkflowExecutor executor,
        IWorkflowDefinitionResolver definitionResolver,
        string workerId,
        IOptions<WorkflowStartRequestLeaseOptions> leaseOptions,
        ILogger? logger = null)
    {
        _db = db;
        _executor = executor;
        _definitionResolver = definitionResolver;
        _workerId = workerId;
        _leaseOptions = leaseOptions.Value;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(string runId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - _leaseOptions.LeaseTimeout;

        if (_db.Database.IsRelational())
        {
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE WorkflowStartRequests
SET Status = {RunningStatus}, ClaimedAt = {now}, ClaimedBy = {_workerId}
WHERE RunId = {runId} AND (
    (Status = {RequestedStatus} AND ClaimedAt IS NULL) OR
    (Status = {RunningStatus} AND ClaimedAt IS NOT NULL AND ClaimedAt < {staleBefore})
)
", cancellationToken);

            return affected == 1;
        }

        // Fallback path for in-memory provider (tests)
        var entity = await _db.WorkflowStartRequests.FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (entity is null)
            return false;

        var eligible =
            (string.Equals(entity.Status, RequestedStatus, StringComparison.Ordinal) && entity.ClaimedAt is null) ||
            (string.Equals(entity.Status, RunningStatus, StringComparison.Ordinal) && entity.ClaimedAt is not null && entity.ClaimedAt < staleBefore);
        if (!eligible)
            return false;

        entity.Status = RunningStatus;
        entity.ClaimedAt = now;
        entity.ClaimedBy = _workerId;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryRenewLeaseAsync(string runId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (_db.Database.IsRelational())
        {
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE WorkflowStartRequests
SET ClaimedAt = {now}
WHERE RunId = {runId} AND Status = {RunningStatus} AND ClaimedBy = {_workerId} AND ClaimedAt IS NOT NULL
", cancellationToken);
            return affected == 1;
        }

        // Fallback path for in-memory provider (tests)
        var entity = await _db.WorkflowStartRequests.FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (entity is null)
            return false;
        if (!string.Equals(entity.Status, RunningStatus, StringComparison.Ordinal))
            return false;
        if (!string.Equals(entity.ClaimedBy, _workerId, StringComparison.Ordinal))
            return false;
        if (entity.ClaimedAt is null)
            return false;

        entity.ClaimedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ProcessClaimedAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using var tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var request = await _db.WorkflowStartRequests
            .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (request is null)
            return;

        // Ownership enforcement: processing is valid only for the current lease owner.
        // Ownership loss is a coordination event; stop safely without writing terminal lifecycle state.
        if (!string.Equals(request.Status, RunningStatus, StringComparison.Ordinal))
            return;
        if (request.ClaimedAt is null)
            return;
        if (!string.Equals(request.ClaimedBy, _workerId, StringComparison.Ordinal))
            return;

        var instance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(x => x.Id == request.WorkflowInstanceId, cancellationToken);
        var run = await _db.OperationRuns
            .FirstOrDefaultAsync(x => x.Id == request.RunId, cancellationToken);

        if (instance is null || run is null)
        {
            request.Status = FailedStatus;
            request.LastError = "Missing WorkflowInstance or OperationRun record for start request.";
            await _db.SaveChangesAsync(cancellationToken);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
            return;
        }

        // Transition instance/run into running.
        instance.Status = RunningStatus;
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        run.Status = RunningStatus;

        try
        {
            var definition = await ResolveDefinitionOrThrowAsync(request.WorkflowDefinitionId, cancellationToken);
            var parameters = DeserializeParameters(request.ParametersJson);

            foreach (var stepName in definition.StepNames)
            {
                // Heartbeat at step boundary: ensure we still own the lease before mutating authoritative state.
                if (!await TryRenewLeaseAsync(runId, cancellationToken))
                    return;

                instance.CurrentStepName = stepName;
                instance.Version += 1;
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                // Execute step; null result is acceptable (no-op handler).
                var domainInstance = new WorkflowInstance(
                    new WorkflowInstanceId(instance.Id),
                    new WorkflowDefinitionId(instance.DefinitionId),
                    instance.Status,
                    instance.CreatedAt,
                    instance.UpdatedAt,
                    instance.CorrelationKey,
                    instance.CurrentStepName,
                    instance.Version);

                await _executor.ExecuteStepAsync(definition, stepName, domainInstance, parameters, cancellationToken);

                // Heartbeat after step completion: detect ownership loss (e.g. reclaim) and stop safely.
                if (!await TryRenewLeaseAsync(runId, cancellationToken))
                    return;
            }

            instance.CurrentStepName = null;
            instance.Status = CompletedStatus;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            run.Status = CompletedStatus;
            run.CompletedAt = DateTimeOffset.UtcNow;
            request.Status = CompletedStatus;
            request.LastError = null;

            await _db.SaveChangesAsync(cancellationToken);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Start request execution failed. RunId={RunId}", runId);

            instance.Status = FailedStatus;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            run.Status = FailedStatus;
            run.CompletedAt = DateTimeOffset.UtcNow;
            request.Status = FailedStatus;
            request.LastError = Truncate(ex.Message, 2048);

            await _db.SaveChangesAsync(cancellationToken);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
        }
    }

    private async ValueTask<WorkflowDefinition> ResolveDefinitionOrThrowAsync(string workflowDefinitionId, CancellationToken cancellationToken)
    {
        var id = new WorkflowDefinitionId(workflowDefinitionId);
        var definition = await _definitionResolver.GetByIdAsync(id, cancellationToken);
        if (definition is null)
            throw new InvalidOperationException($"Unknown workflow definition id '{workflowDefinitionId}'.");
        return definition;
    }

    private static IReadOnlyDictionary<string, object?>? DeserializeParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        // Use JsonElement values to avoid fragile object typing.
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson, JsonOptions);
        if (raw is null || raw.Count == 0)
            return null;

        return raw.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
    }

    private static string Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Length <= maxLen ? s : s[..maxLen];
    }
}

