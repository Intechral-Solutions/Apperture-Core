using System.Net;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>What one authoritative <c>GET auth/me</c> says about the operator's session.</summary>
public enum SessionProbeResult
{
    /// <summary>200: there is a session and it maps to a usable account. Whatever returned 401 was endpoint-specific.</summary>
    Authenticated,

    /// <summary>401: positive evidence that the session is gone. The only outcome that leads to sign-in.</summary>
    Unauthenticated,

    /// <summary>403: a session exists but its principal has no usable Apperture account.</summary>
    AccountUnavailable,

    /// <summary>Network failure, timeout, 5xx or any unexpected status: availability, never authentication.</summary>
    ServiceUnavailable,
}

/// <summary>The single authoritative session re-check used by <see cref="SessionRecoveryCoordinator"/>.</summary>
public interface ISessionProbe
{
    Task<SessionProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ISessionProbe"/> over a credentials-only <see cref="HttpClient"/> that is deliberately NOT the
/// application client: it has no recovery handler, so the probe can never report its own 401 back to the
/// coordinator that issued it.
/// </summary>
public sealed class HttpSessionProbe : ISessionProbe
{
    private readonly HttpClient _http;

    public HttpSessionProbe(HttpClient http) => _http = http;

    public async Task<SessionProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/me");
            // A cached answer is not evidence about the session right now.
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => SessionProbeResult.Unauthenticated,
                HttpStatusCode.Forbidden => SessionProbeResult.AccountUnavailable,
                _ when response.IsSuccessStatusCode => SessionProbeResult.Authenticated,
                _ => SessionProbeResult.ServiceUnavailable,
            };
        }
        catch (Exception)
        {
            // Connection refused, DNS, CORS-blocked 5xx, timeout/cancellation: no positive evidence of anything.
            return SessionProbeResult.ServiceUnavailable;
        }
    }
}
