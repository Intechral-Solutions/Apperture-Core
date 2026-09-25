using Apperture.Shared.Kernel;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Adds the background-activity marker when the current async flow is inside <see cref="BackgroundRequest"/>.
/// This is the only place the header is written. SEC-1B1 will add the platform request header here later.
/// </summary>
public sealed class BrowserRequestHeadersHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (BackgroundRequest.IsActive)
            request.Headers.TryAddWithoutValidation(BackgroundActivity.HeaderName, BackgroundActivity.HeaderValue);
        return base.SendAsync(request, cancellationToken);
    }
}
