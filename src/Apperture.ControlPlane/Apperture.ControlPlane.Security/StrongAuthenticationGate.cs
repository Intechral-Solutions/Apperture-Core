using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class StrongAuthenticationGate : IStrongAuthenticationGate
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthenticationAssuranceEvaluator _evaluator;
    private readonly IOptionsMonitor<EntraAuthOptions> _entra;

    public StrongAuthenticationGate(
        IHttpContextAccessor httpContextAccessor,
        IAuthenticationAssuranceEvaluator evaluator,
        IOptionsMonitor<EntraAuthOptions> entra)
    {
        _httpContextAccessor = httpContextAccessor;
        _evaluator = evaluator;
        _entra = entra;
    }

    /// <inheritdoc />
    public ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return ValueTask.FromResult(_evaluator.Evaluate(principal));
    }

    /// <inheritdoc />
    public ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStrongAuthenticationConfigured())
        {
            throw new StrongAuthenticationException(
                StrongAuthenticationErrorCodes.NotConfigured,
                "Strong authentication is not configured. A Platform Administrator must set a Microsoft Entra Conditional Access authentication-context identifier on the active Entra registration before local sign-in can be re-enabled.");
        }

        var assurance = _evaluator.Evaluate(_httpContextAccessor.HttpContext?.User);
        if (assurance.RequirementSatisfied && assurance.IsRecent)
            return ValueTask.CompletedTask;

        if (assurance.RequirementSatisfied && !assurance.IsRecent)
        {
            throw new StrongAuthenticationException(
                StrongAuthenticationErrorCodes.Expired,
                "Recent strong authentication has expired. Complete Microsoft Entra step-up and try again.");
        }

        throw new StrongAuthenticationException(
            StrongAuthenticationErrorCodes.Required,
            "This action requires recent strong authentication. Complete Microsoft Entra step-up and try again.");
    }

    private bool IsStrongAuthenticationConfigured()
    {
        var entra = _entra.CurrentValue;
        return EntraConfigurationState.IsUsable(entra)
            && EntraAuthenticationContextId.TryNormalize(entra.AuthenticationContextId) is not null;
    }
}
