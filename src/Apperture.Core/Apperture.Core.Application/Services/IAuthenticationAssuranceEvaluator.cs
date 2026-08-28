using System.Security.Claims;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Reads authentication-assurance claims from the current host principal. The decision is
/// server-derived; flags sent from Blazor WASM are ignored.
/// </summary>
public interface IAuthenticationAssuranceEvaluator
{
    /// <summary>
    /// Evaluates the principal against the configured recent-authentication lifetime.
    /// </summary>
    AuthenticationAssurance Evaluate(ClaimsPrincipal? principal);

    /// <summary>
    /// Evaluates using an explicit lifetime, for tests and expiry checks.
    /// </summary>
    AuthenticationAssurance Evaluate(ClaimsPrincipal? principal, TimeSpan recentLifetime, DateTimeOffset utcNow);
}
