namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// The one HTTP-pipeline interception point for session loss (SEC-1 design gate §12). It sees every response;
/// on a 401 from a protected call it tells the per-tab coordinator and waits for the (shared) decision, then
/// returns the ORIGINAL response untouched.
/// <para>
/// It must not: retry or replay (a request that met 401 is never re-sent, before or after sign-in), alter or
/// swallow a response, look at 403 (an authorization result, not authentication), report the sign-in and bootstrap
/// endpoints (<see cref="AuthRecoveryExclusions"/>), or navigate. Waiting for the decision, rather than
/// firing and forgetting, keeps a page from painting its own raw "401 Unauthorized" for the instant before the
/// coordinator replaces the shell with the recovery surface.
/// </para>
/// </summary>
public sealed class AuthRecoveryHandler : DelegatingHandler
{
    // Resolved lazily: the coordinator depends on the session state, which depends on the very HttpClient this
    // handler belongs to.
    private readonly Func<ISessionRecoveryCoordinator> _coordinator;

    public AuthRecoveryHandler(Func<ISessionRecoveryCoordinator> coordinator) => _coordinator = coordinator;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized
            || AuthRecoveryExclusions.IsExcluded(request.RequestUri))
        {
            return response;
        }

        try
        {
            await _coordinator().ReportUnauthenticatedAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // This caller gave up. The shared decision belongs to the coordinator and carries on.
            response.Dispose();
            throw;
        }
        catch (Exception)
        {
            // Recovery bookkeeping must never fail the caller's own request.
        }

        return response;
    }
}

/// <summary>
/// API endpoints whose 401 is part of signing in, bootstrapping or a separate auth scheme. They never report to
/// session recovery, so the sign-in surfaces cannot recurse into it (design gate §12 exclusion set).
/// </summary>
public static class AuthRecoveryExclusions
{
    private static readonly HashSet<string> ExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth/me",
        // The anonymous local sign-in endpoints only. auth/local/change-password is session-authenticated, so a 401
        // there IS session loss and must report.
        "auth/local/login",
        "auth/local/mfa",
        "auth/break-glass",
        "auth/sign-in-options",
        "auth/forgot-password",
        "auth/reset-password",
        "auth/setup-password",
        "auth/verify-contact-email",
        "access-requests",
    };

    private static readonly string[] PathPrefixes =
    [
        "access-requests/",
        "bootstrap/",
    ];

    /// <summary>True for the sign-in, bootstrap and access-request endpoints (path relative to the API root).</summary>
    public static bool IsExcluded(Uri? requestUri)
    {
        if (requestUri is null)
            return false;

        var path = requestUri.AbsolutePath;
        const string apiSegment = "/api/";
        var index = path.IndexOf(apiSegment, StringComparison.OrdinalIgnoreCase);
        var relative = (index >= 0 ? path[(index + apiSegment.Length)..] : path.TrimStart('/')).TrimEnd('/');

        return ExactPaths.Contains(relative)
            || PathPrefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
