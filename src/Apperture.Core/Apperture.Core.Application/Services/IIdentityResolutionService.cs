using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves the current principal (human or machine) from the ambient auth context.
/// </summary>
public interface IIdentityResolutionService
{
    /// <summary>
    /// Resolves the current actor from the host's authentication context.
    /// </summary>
    ValueTask<IActor?> ResolveAsync(CancellationToken cancellationToken = default);
}
