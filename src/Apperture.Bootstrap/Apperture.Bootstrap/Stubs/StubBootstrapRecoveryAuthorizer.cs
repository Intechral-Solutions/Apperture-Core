using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// Stub implementation. Denies all recovery requests.
/// </summary>
public sealed class StubBootstrapRecoveryAuthorizer : IBootstrapRecoveryAuthorizer
{
    public Task<BootstrapRecoveryAuthorizationResult> AuthorizeAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BootstrapRecoveryAuthorizationResult { Authorized = false, Reason = "Recovery not configured." });
}
