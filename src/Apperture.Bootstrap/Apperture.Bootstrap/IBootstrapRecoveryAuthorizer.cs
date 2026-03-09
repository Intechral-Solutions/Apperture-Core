using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Authorizes recovery actions. Break-glass only, not routine admin.
/// </summary>
public interface IBootstrapRecoveryAuthorizer
{
    /// <summary>Authorizes the recovery request.</summary>
    Task<BootstrapRecoveryAuthorizationResult> AuthorizeAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default);
}
