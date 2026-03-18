using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// Ensures browser fetch includes cookies for cross-origin API calls (dev: UI :9001 -> API :9000).
/// </summary>
public sealed class IncludeCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}

