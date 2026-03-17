using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorizes recovery actions using configured token. For RestorePlatformAccess, request proof must match Auth:RecoveryToken or Bootstrap:RecoveryToken.
/// Denies all other action types or when token is not configured. Does not include secrets in audit.
/// </summary>
public sealed class ConfigRecoveryAuthorizer : IBootstrapRecoveryAuthorizer
{
    private readonly IConfiguration _configuration;

    public ConfigRecoveryAuthorizer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public Task<BootstrapRecoveryAuthorizationResult> AuthorizeAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ActionType != BootstrapRecoveryAction.RestorePlatformAccess)
        {
            return Task.FromResult(new BootstrapRecoveryAuthorizationResult { Authorized = false, Reason = "Only RestorePlatformAccess is supported." });
        }

        var configuredToken = _configuration["Auth:RecoveryToken"] ?? _configuration["Bootstrap:RecoveryToken"];
        if (string.IsNullOrEmpty(configuredToken))
        {
            return Task.FromResult(new BootstrapRecoveryAuthorizationResult { Authorized = false, Reason = "Recovery token not configured." });
        }

        var proof = request.Proof is string s ? s : request.Proof?.ToString();
        if (string.IsNullOrEmpty(proof) || !string.Equals(proof.Trim(), configuredToken.Trim(), StringComparison.Ordinal))
        {
            return Task.FromResult(new BootstrapRecoveryAuthorizationResult { Authorized = false, Reason = "Recovery not authorized." });
        }

        return Task.FromResult(new BootstrapRecoveryAuthorizationResult { Authorized = true });
    }
}
