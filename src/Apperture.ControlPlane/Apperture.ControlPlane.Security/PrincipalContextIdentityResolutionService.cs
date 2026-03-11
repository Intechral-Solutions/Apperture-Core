using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// IIdentityResolutionService implementation that delegates to IPrincipalContext.
/// Keeps a single resolution path for the current actor.
/// </summary>
public sealed class PrincipalContextIdentityResolutionService : IIdentityResolutionService
{
    private readonly IPrincipalContext _principalContext;

    public PrincipalContextIdentityResolutionService(IPrincipalContext principalContext)
    {
        _principalContext = principalContext;
    }

    /// <inheritdoc />
    public ValueTask<IActor?> ResolveAsync(CancellationToken cancellationToken = default) =>
        _principalContext.GetCurrentActorAsync(cancellationToken);
}
