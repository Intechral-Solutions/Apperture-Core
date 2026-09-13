using System.Security.Claims;
using Apperture.Core.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class AuthenticationAssuranceEvaluator : IAuthenticationAssuranceEvaluator
{
    private readonly IOptionsMonitor<AuthenticationAssuranceOptions> _options;
    private readonly TimeProvider _time;
    private readonly IHostEnvironment? _environment;

    public AuthenticationAssuranceEvaluator(
        IOptionsMonitor<AuthenticationAssuranceOptions> options,
        TimeProvider time,
        IHostEnvironment? environment = null)
    {
        _options = options;
        _time = time;
        _environment = environment;
    }

    /// <inheritdoc />
    public AuthenticationAssurance Evaluate(ClaimsPrincipal? principal)
    {
        var lifetime = _options.CurrentValue.RecentStrongAuthenticationLifetime;
        if (lifetime <= TimeSpan.Zero)
            lifetime = AuthenticationAssuranceOptions.DefaultRecentLifetime;
        return Evaluate(principal, lifetime, _time.GetUtcNow());
    }

    /// <inheritdoc />
    public AuthenticationAssurance Evaluate(ClaimsPrincipal? principal, TimeSpan recentLifetime, DateTimeOffset utcNow)
    {
        var mechanism = AuthenticationAssuranceClaims.TryGetMechanism(principal);
        var established = AuthenticationAssuranceClaims.TryGetEstablishedAt(principal);
        var context = AuthenticationAssuranceClaims.TryGetContext(principal);
        var simulated = AuthenticationAssuranceClaims.IsDevelopmentSimulated(principal);

        if (string.IsNullOrEmpty(mechanism) || established is null)
            return AuthenticationAssurance.None;

        // A simulated claim is only meaningful in Development. Production must never treat it as satisfied.
        var simulationHonored = simulated && _environment?.IsDevelopment() == true;
        if (simulated && !simulationHonored)
            return new AuthenticationAssurance(mechanism, false, established, context, false, DevelopmentSimulated: true);

        var requirementSatisfied = true;
        var age = utcNow - established.Value;
        var isRecent = age >= TimeSpan.Zero && age <= recentLifetime;
        return new AuthenticationAssurance(mechanism, requirementSatisfied, established, context, isRecent, simulationHonored);
    }
}
