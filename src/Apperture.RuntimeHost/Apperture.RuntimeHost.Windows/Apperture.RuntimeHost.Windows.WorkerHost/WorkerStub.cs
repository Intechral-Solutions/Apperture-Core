using Apperture.Core.Security.Secrets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Stub worker service; demonstrates ISecretResolver is available for runtime secret resolution.
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetService<ISecretResolver>();
        if (resolver is not null)
            _logger.LogInformation("Runtime secret resolution available (ISecretResolver).");
        else
            _logger.LogWarning("ISecretResolver not registered.");

        return Task.CompletedTask;
    }
}
