using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Executes the recovery flow: authorize, audit, then perform recovery action.
/// Break-glass only; routine admin must not use this.
/// </summary>
public interface IBootstrapRecoveryService
{
    /// <summary>
    /// Authorizes the request, writes recovery audit, and optionally performs the recovery action.
    /// </summary>
    Task<BootstrapRecoveryResult> ExecuteRecoveryAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default);
}
