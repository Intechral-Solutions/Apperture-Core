using Microsoft.AspNetCore.Http;

namespace Apperture.Management.Host;

internal static class ClickjackingHeaders
{
    public const string ContentSecurityPolicy = "frame-ancestors 'none'";

    public static void Apply(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.XFrameOptions = "DENY";
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
    }
}
