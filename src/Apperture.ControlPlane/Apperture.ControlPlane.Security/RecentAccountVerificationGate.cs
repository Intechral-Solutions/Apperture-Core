using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

public sealed class RecentAccountVerificationGate : IRecentAccountVerificationGate
{
    private readonly IHttpContextAccessor _http;
    private readonly IAuthenticationAssuranceEvaluator _assurance;
    private readonly AuthenticationAssuranceOptions _options;

    public RecentAccountVerificationGate(
        IHttpContextAccessor http,
        IAuthenticationAssuranceEvaluator assurance,
        IOptions<AuthenticationAssuranceOptions> options)
    {
        _http = http;
        _assurance = assurance;
        _options = options.Value;
    }

    public RecentAccountVerification GetCurrent()
    {
        var principal = _http.HttpContext?.User;
        var lifetime = _options.RecentStrongAuthenticationLifetime;
        if (lifetime <= TimeSpan.Zero)
            lifetime = AuthenticationAssuranceOptions.DefaultRecentLifetime;

        var passwordAt = RecentAccountVerificationClaims.TryGetEstablishedAt(principal);
        var passwordMechanism = RecentAccountVerificationClaims.TryGetMechanism(principal);
        if (passwordAt is not null && !string.IsNullOrWhiteSpace(passwordMechanism))
        {
            var expires = passwordAt.Value + lifetime;
            if (DateTimeOffset.UtcNow <= expires)
            {
                return new RecentAccountVerification(
                    passwordMechanism,
                    passwordAt,
                    expires,
                    IsSatisfied: true);
            }
        }

        var strong = _assurance.Evaluate(principal);
        if (strong.RequirementSatisfied && strong.IsRecent && strong.EstablishedAtUtc is not null)
        {
            return new RecentAccountVerification(
                strong.Mechanism,
                strong.EstablishedAtUtc,
                strong.EstablishedAtUtc.Value + lifetime,
                IsSatisfied: true);
        }

        return RecentAccountVerification.None;
    }

    public void Require()
    {
        if (GetCurrent().IsSatisfied)
            return;
        throw new RecentAccountVerificationException(
            RecentAccountVerificationErrorCodes.Required,
            "Verify your identity to manage these account settings.");
    }
}
