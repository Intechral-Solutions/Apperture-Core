using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Development-only: ensures a configured dev test user (e.g. "tenny") exists with platform-admin role and local credential.
/// Runs after migrations; idempotent. Disabled when DevTestUser:Enabled is false or not in Development.
/// </summary>
public sealed class DevTestUserBootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<DevTestUserOptions> _options;
    private readonly ILogger<DevTestUserBootstrapHostedService>? _logger;

    public DevTestUserBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IOptions<DevTestUserOptions> options,
        ILogger<DevTestUserBootstrapHostedService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return;

        var opts = _options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Username))
            return;

        var username = LocalAuthHelpers.NormalizeUsername(opts.Username);
        if (EmergencyAccessIdentity.IsReservedLocalUsername(username))
        {
            _logger?.LogWarning("Dev test user username is reserved for Emergency Access; skipped.");
            return;
        }
        var displayName = string.IsNullOrWhiteSpace(opts.DisplayName) ? opts.Username : opts.DisplayName.Trim();

        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var localCredRepo = scope.ServiceProvider.GetService<ILocalCredentialRepository>();
        var passwordHasher = scope.ServiceProvider.GetService<ILocalPasswordHasher>();

        var existing = await userRepo.GetByIdentityLinkAsync(IdentitySource.Local, username, cancellationToken);
        if (existing is not null)
        {
            await EnsureDevPasswordAsync(existing.Id, opts, localCredRepo, passwordHasher, username, cancellationToken)
                .ConfigureAwait(false);

            var roles = await roleRepo.GetForUserAsync(existing.Id, cancellationToken);
            var hasAdmin = roles.Any(r => string.Equals(r.Id.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.Ordinal));
            if (hasAdmin)
            {
                _logger?.LogDebug("Dev test user {Username} already exists with platform-admin.", username);
                return;
            }

            var adminRole = await roleRepo.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken);
            if (adminRole is null)
            {
                _logger?.LogWarning("Platform administrator role not found; cannot assign to dev test user.");
                return;
            }

            await roleRepo.AssignToUserAsync(existing.Id, new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken);
            _logger?.LogInformation("Assigned platform-admin role to dev test user {Username}.", username);
            return;
        }

        var adminRoleCheck = await roleRepo.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), cancellationToken);
        if (adminRoleCheck is null)
        {
            _logger?.LogWarning("Platform administrator role not found; cannot create dev test user.");
            return;
        }

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName,
            new[] { new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null) },
            new[] { new RoleId(BootstrapConstants.PlatformAdminRoleId) },
            IsBreakGlassEligible: false);

        await userRepo.AddAsync(user, cancellationToken);

        if (!string.IsNullOrEmpty(opts.Password) && localCredRepo is not null && passwordHasher is not null)
        {
            if (!TryHashDevPassword(opts.Password, username, displayName, passwordHasher, out var hash, out var salt))
                return;
            await localCredRepo.SetPasswordAsync(userId, hash, salt, passwordHasher.AlgorithmName, cancellationToken);
        }
        else if (string.IsNullOrEmpty(opts.Password))
        {
            _logger?.LogWarning("DevTestUser:Password not set; dev test user {Username} has no local credential. Set DevTestUser:Password in appsettings.Development.json.", username);
        }

        _logger?.LogInformation("Created dev test user {Username} ({DisplayName}) with platform-admin role.", username, displayName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureDevPasswordAsync(
        UserId userId,
        DevTestUserOptions opts,
        ILocalCredentialRepository? localCredRepo,
        ILocalPasswordHasher? passwordHasher,
        string username,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(opts.Password) || localCredRepo is null || passwordHasher is null)
            return;

        if (!TryHashDevPassword(opts.Password, username, null, passwordHasher, out var hash, out var salt))
            return;
        await localCredRepo.SetPasswordAsync(userId, hash, salt, passwordHasher.AlgorithmName, cancellationToken)
            .ConfigureAwait(false);
        _logger?.LogDebug("Ensured DevTestUser password for {Username}.", username);
    }

    private bool TryHashDevPassword(
        string password,
        string username,
        string? displayName,
        ILocalPasswordHasher passwordHasher,
        out string hash,
        out string salt)
    {
        try
        {
            LocalPasswordPolicy.Require(password, LocalPasswordPolicy.ForLocalIdentity(username, displayName));
        }
        catch (PasswordPolicyException ex)
        {
            _logger?.LogWarning("DevTestUser password for {Username} was rejected: {Reason}", username, ex.Message);
            hash = "";
            salt = "";
            return false;
        }

        (hash, salt) = passwordHasher.HashPassword(password);
        return true;
    }
}
