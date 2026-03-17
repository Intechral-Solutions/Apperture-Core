using System.Text.Json;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger? _logger;
    private readonly string _workerId;

    public WorkflowStartRequestProcessor(
        AppertureDbContext db,
        IWorkflowExecutor executor,
        string workerId,
        ILogger? logger = null)
    {
        _db = db;
        _executor = executor;
        _workerId = workerId;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(string runId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_db.Database.IsRelational())
        {
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE WorkflowStartRequests
SET Status = {RunningStatus}, ClaimedAt = {now}, ClaimedBy = {_workerId}
WHERE RunId = {runId} AND Status = {RequestedStatus} AND ClaimedAt IS NULL
", cancellationToken);

            return affected == 1;
        }

        // Fallback path for in-memory provider (tests)
        var entity = await _db.WorkflowStartRequests.FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
        if (entity is null)
            return false;
        if (!string.Equals(entity.Status, RequestedStatus, StringComparison.Ordinal) || entity.ClaimedAt is not null)
            return false;

        entity.Status = RunningStatus;
        entity.ClaimedAt = now;
        entity.ClaimedBy = _workerId;
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

        // If another worker already completed it (shouldn't happen post-claim), bail.
        if (!string.Equals(request.Status, RunningStatus, StringComparison.Ordinal))
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
            var definition = ResolveDefinitionOrThrow(request.WorkflowDefinitionId);
            var parameters = DeserializeParameters(request.ParametersJson);

            foreach (var stepName in definition.StepNames)
            {
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

    private static WorkflowDefinition ResolveDefinitionOrThrow(string workflowDefinitionId)
    {
        // Minimal resolver for the first end-to-end slice.
        // Expand later to a real module-based definition resolver.
        if (string.Equals(workflowDefinitionId, "Sample.HelloWorld.HelloWorld", StringComparison.Ordinal))
        {
            return new WorkflowDefinition(
                new WorkflowDefinitionId("Sample.HelloWorld.HelloWorld"),
                "HelloWorld",
                "Sample.HelloWorld",
                new[] { "Validate", "DoWork", "Complete" });
        }

        throw new InvalidOperationException($"Unknown workflow definition id '{workflowDefinitionId}'.");
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

