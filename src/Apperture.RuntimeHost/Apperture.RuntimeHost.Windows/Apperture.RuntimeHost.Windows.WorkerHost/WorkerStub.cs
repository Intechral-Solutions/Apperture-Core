using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Stub worker service; demonstrates ISecretResolver and slot-bound step execution.
/// Runs one demo step (Sample.HelloWorld / DoWork) that resolves the "Db" slot to prove the execution path.
/// </summary>
public sealed class WorkerStub : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkerStub> _logger;

    public WorkerStub(IServiceScopeFactory scopeFactory, ILogger<WorkerStub> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetService<ISecretResolver>();
        if (resolver is not null)
            _logger.LogInformation("Runtime secret resolution available (ISecretResolver).");
        else
            _logger.LogWarning("ISecretResolver not registered.");

        // Run one demo step to prove slot-bound resolution in execution
        var executor = scope.ServiceProvider.GetService<IWorkflowExecutor>();
        if (executor is not null)
        {
            var definition = new WorkflowDefinition(
                new WorkflowDefinitionId("Sample.HelloWorld.HelloWorld"),
                "HelloWorld",
                "Sample.HelloWorld",
                new[] { "Validate", "DoWork", "Complete" });
            var instance = new WorkflowInstance(
                new WorkflowInstanceId(Guid.NewGuid().ToString("N")[..24]),
                definition.Id,
                "Running",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                "DoWork",
                1);
            var result = await executor.ExecuteStepAsync(definition, "DoWork", instance, null, stoppingToken);
            _logger.LogInformation("Demo step DoWork completed (slot-bound resolution exercised). Result: {Result}", result is null ? "null" : "ok");
        }
        else
            _logger.LogWarning("IWorkflowExecutor not registered.");
    }
}
