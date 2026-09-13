namespace Apperture.Core.Application.Services;

/// <summary>
/// Application-layer gate for operations that require recent strong authentication in addition
/// to ordinary permission checks.
/// </summary>
public interface IStrongAuthenticationGate
{
    /// <summary>Current actor's server-derived assurance, or <see cref="AuthenticationAssurance.None"/>.</summary>
    ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="StrongAuthenticationException"/> when the current actor lacks recent
    /// strong authentication, or when no usable strong-authentication mechanism is configured.
    /// </summary>
    ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default);
}
