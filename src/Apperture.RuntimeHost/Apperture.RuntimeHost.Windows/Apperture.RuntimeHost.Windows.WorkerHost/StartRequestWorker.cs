using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.ModuleSdk.Abstractions;
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
    private const string RequestedStatus = "requested";
    private const string RunningStatus = "running";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartRequestWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly string _workerId;
    private readonly IOptions<WorkflowStartRequestLeaseOptions> _leaseOptions;

    public StartRequestWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StartRequestWorker> logger,
        IOptions<WorkflowStartRequestLeaseOptions> leaseOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(2);
        _workerId = $"windows-worker:{Environment.MachineName}:{Guid.NewGuid():N}"[..64];
        _leaseOptions = leaseOptions;
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
                var executor = scope.ServiceProvider.GetRequiredService<Apperture.RuntimePlane.Abstractions.IWorkflowExecutor>();
                var definitionResolver = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionResolver>();
                var processor = new WorkflowStartRequestProcessor(db, executor, definitionResolver, _workerId, _leaseOptions, _logger);

                // Best-effort: attempt a small batch each poll.
                var now = DateTimeOffset.UtcNow;
                var staleBefore = now - _leaseOptions.Value.LeaseTimeout;
                var pending = await db.WorkflowStartRequests.AsNoTracking()
                    .Where(x =>
                        (x.Status == RequestedStatus && x.ClaimedAt == null) ||
                        (x.Status == RunningStatus && x.ClaimedAt != null && x.ClaimedAt < staleBefore))
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

