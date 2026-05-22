using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Instance-plane worker that consumes persisted workflow start requests and drives a minimal execution lifecycle.
/// </summary>
public sealed class StartRequestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartRequestWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly string _workerId;
    private readonly string? _runtimeId;
    private readonly RuntimeShutdownRequestApplier _shutdownApplier = new();
    private readonly IOptions<WorkflowStartRequestLeaseOptions> _leaseOptions;
    private readonly IOptions<OperationStartRequestLeaseOptions> _operationLeaseOptions;
    private readonly IHostApplicationLifetime _appLifetime;

    public StartRequestWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StartRequestWorker> logger,
        IOptions<WorkflowStartRequestLeaseOptions> leaseOptions,
        IOptions<OperationStartRequestLeaseOptions> operationLeaseOptions,
        IOptions<RuntimeHostIdentityOptions> runtimeHostIdentityOptions,
        IHostApplicationLifetime appLifetime)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(2);
        // Truncate safely: string length may be < 64 depending on machine name format.
        var rawWorkerId = $"windows-worker:{Environment.MachineName}:{Guid.NewGuid():N}";
        _workerId = rawWorkerId.Length <= 64 ? rawWorkerId : rawWorkerId[..64];
        _runtimeId = runtimeHostIdentityOptions.Value.RuntimeId;
        _leaseOptions = leaseOptions;
        _operationLeaseOptions = operationLeaseOptions;
        _appLifetime = appLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StartRequestWorker started. WorkerId={WorkerId}", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

                // Runtime governance proving ground: shutdown requests stop the host.
                if (await _shutdownApplier.TryApplyQueuedShutdownAsync(db, _runtimeId, _workerId, stoppingToken).ConfigureAwait(false))
                {
                    _logger.LogWarning("Applied runtime shutdown request. Stopping worker host.");
                    _appLifetime.StopApplication();
                    return;
                }

                var executor = scope.ServiceProvider.GetRequiredService<Apperture.RuntimePlane.Abstractions.IWorkflowExecutor>();
                var definitionResolver = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionResolver>();
                var moduleRegistry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>();
                var audit = scope.ServiceProvider.GetService<IAuditService>();
                var integrationExecution = scope.ServiceProvider.GetService<IOptions<IntegrationExecutionOptions>>();
                var lifecycleEmitter = scope.ServiceProvider.GetService<ExecutionLifecyclePlatformEventEmitter>();
                var processor = new WorkflowStartRequestProcessor(
                    db,
                    executor,
                    definitionResolver,
                    _workerId,
                    _leaseOptions,
                    _logger,
                    moduleRegistry,
                    audit,
                    integrationExecution,
                    lifecycleEmitter);
                var operationRegistry = scope.ServiceProvider.GetRequiredService<IOperationRegistryService>();
                var operationProcessor = new OperationStartRequestProcessor(
                    db,
                    executor,
                    operationRegistry,
                    _workerId,
                    _operationLeaseOptions,
                    _logger,
                    moduleRegistry,
                    audit,
                    integrationExecution,
                    lifecycleEmitter);

                // Best-effort: attempt a small batch each poll.
                var now = DateTimeOffset.UtcNow;
                var staleBefore = now - _leaseOptions.Value.LeaseTimeout;
                var pending = await db.WorkflowStartRequests.AsNoTracking()
                    .Where(x =>
                        (x.Status == WorkflowLifecycleStatusNames.Requested && x.ClaimedAt == null) ||
                        (x.Status == WorkflowLifecycleStatusNames.Running && x.ClaimedAt != null && x.ClaimedAt < staleBefore) ||
                        (x.Status == WorkflowLifecycleStatusNames.Parked && db.WorkflowExternalControlRequests.Any(c =>
                            c.RunId == x.RunId &&
                            c.ControlKind == WorkflowExternalControlKindNames.Resume &&
                            c.Status == WorkflowExternalControlStatusNames.Queued)))
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => x.RunId)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var runId in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var claimed = await processor.TryClaimAsync(runId, stoppingToken);
                    if (!claimed)
                        continue;

                    await processor.ProcessClaimedAsync(runId, stoppingToken);
                }

                // Best-effort: attempt to process a small batch of operation starts each poll.
                var operationStaleBefore = now - _operationLeaseOptions.Value.LeaseTimeout;
                var pendingOperations = await db.OperationStartRequests.AsNoTracking()
                    .Where(x =>
                        (x.Status == WorkflowLifecycleStatusNames.Requested && x.ClaimedAt == null) ||
                        (x.Status == WorkflowLifecycleStatusNames.Running && x.ClaimedAt != null && x.ClaimedAt < operationStaleBefore))
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => x.RunId)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var runId in pendingOperations)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var claimed = await operationProcessor.TryClaimAsync(runId, stoppingToken);
                    if (!claimed)
                        continue;

                    await operationProcessor.ProcessClaimedAsync(runId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartRequestWorker loop error.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("StartRequestWorker stopped. WorkerId={WorkerId}", _workerId);
    }

}

