using Apperture.Core.Application.Audit;
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
/// AUD-1.2: every user this step creates and every role it assigns commits with its audit row, recorded as
/// <c>system:bootstrap-configuration</c> (see <see cref="BootstrapPlatformAdminAssignment"/>).
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
        var unit = scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>();
        await BootstrapPlatformAdminAssignment.ExecuteAsync(_options.Value, userRepo, roleRepo, _logger, unit, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
