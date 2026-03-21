using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// At host startup, logs and optionally audits non-effective module composition (Epic 27).
/// </summary>
public sealed class ModuleCompositionBootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModuleCompositionBootstrapHostedService>? _logger;

    public ModuleCompositionBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ModuleCompositionBootstrapHostedService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistryService>();
        var audit = scope.ServiceProvider.GetService<IAuditService>();

        var modules = await registry.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var degraded = modules
            .Where(m => m is { Installed: true, Enabled: true, EffectivelyActive: false })
            .ToList();

        foreach (var m in degraded)
        {
            var codes = string.Join(",", m.ValidationIssues.Select(x => x.Code));
            _logger?.LogWarning(
                "Runtime composition: module {ModuleId} is persisted enabled but not effectively executable for execution. ValidationIssues={Issues}",
                m.Descriptor.ModuleId,
                codes);
        }

        if (degraded.Count == 0)
            return;

        if (audit is null)
            return;

        var summary = string.Join(
            "; ",
            degraded.Select(m =>
                $"{m.Descriptor.ModuleId}:[{string.Join(",", m.ValidationIssues.Select(i => i.Code))}]"));

        await audit.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("system"),
                LifecycleExecutionAuditActionNames.RuntimeCompositionInvalidAtStartup,
                Resource: null,
                Outcome: "Degraded",
                Timestamp: DateTimeOffset.UtcNow,
                new CorrelationId("module-composition-bootstrap"),
                summary),
            cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
