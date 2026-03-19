using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;

namespace Apperture.ControlPlane.Security;

public sealed class AuthorizationEvaluationService : IAuthorizationEvaluationService
{
    private readonly IAuthorizationService _authorizationService;

    public AuthorizationEvaluationService(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
        _authorizationService.EvaluateAsync(
            context.Actor,
            context.Permission,
            context.Resource,
            context.ResourceIdentity,
            cancellationToken);
}

