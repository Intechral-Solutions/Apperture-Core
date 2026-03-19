using Apperture.Core.Domain.Governance;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Shared authorization evaluation seam for consistent behavior across layers.
/// </summary>
public interface IAuthorizationEvaluationService
{
    ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default);
}

