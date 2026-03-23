using Apperture.Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Startup: ensures configured bootstrap principals are assigned to the system-managed Platform Administrator role.
/// Idempotent; delegates to <see cref="BootstrapPlatformAdminAssignment.ExecuteAsync"/>.
/// </summary>
/// <remarks>
/// User-facing role assignment via API goes through <see cref="UserManagementService.AssignRoleAsync"/> and emits audit events.
/// Bootstrap assignment is intentional platform setup; operators should review host logs for
/// "Assigned Platform Administrator role" / "Created bootstrap user". A future pass may add durable audit if required.
/// </remarks>
public sealed class BootstrapPlatformAdminAssignmentHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BootstrapPlatformAdminOptions> _options;
    private readonly ILogger<BootstrapPlatformAdminAssignmentHostedService>? _logger;

    public BootstrapPlatformAdminAssignmentHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BootstrapPlatformAdminOptions> options,
        ILogger<BootstrapPlatformAdminAssignmentHostedService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        await BootstrapPlatformAdminAssignment.ExecuteAsync(_options.Value, userRepo, roleRepo, _logger, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
