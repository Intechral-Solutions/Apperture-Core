namespace Apperture.Management.Host;

/// <summary>
/// Endpoint metadata for a host auth endpoint the browser reaches by native navigation (a top-level form post or link),
/// not through the Management UI's HTTP client. Rule N applies: an unsafe method needs the exact trusted <c>Origin</c>,
/// and any method is refused when the browser marks it <c>Sec-Fetch-Site: cross-site</c>. The platform request header is
/// not required, because a navigation cannot carry it.
/// </summary>
internal sealed class NativeNavigationEndpoint
{
}
