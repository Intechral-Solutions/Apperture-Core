using Apperture.Shared.Kernel;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// The only writer of Management UI request headers. Every call gets the platform request marker.
/// A background scope also gets the non-renewing activity marker. Features do not add either header.
/// </summary>
public sealed class BrowserRequestHeadersHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(BrowserRequest.HeaderName, BrowserRequest.HeaderValue);
        if (BackgroundRequest.IsActive)
            request.Headers.TryAddWithoutValidation(BackgroundActivity.HeaderName, BackgroundActivity.HeaderValue);
        return base.SendAsync(request, cancellationToken);
    }
}
