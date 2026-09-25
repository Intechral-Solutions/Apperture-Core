using Apperture.Management.Ui.Shared.Auth;
using Microsoft.Extensions.Configuration;

namespace Apperture.Management.Host;

/// <summary>
/// Builds absolute UI URLs for browser redirects out of the OIDC callback. The management UI is a
/// separate origin from this host, so the base URL comes from configuration with a development default.
/// The authority is always the configured UI origin: a request value never supplies it.
/// </summary>
internal static class ManagementUiRedirects
{
    public const string BaseUrlConfigurationKey = "ShellUi:ManagementUiUrl";
    private const string DevelopmentBaseUrl = "http://localhost:9001";

    public static string ResolveBaseUrl(IConfiguration? configuration)
    {
        var configured = configuration?[BaseUrlConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? DevelopmentBaseUrl
            : configured.TrimEnd('/');
    }

    /// <summary>
    /// <paramref name="relativePath"/> must already be trusted: a host constant (<c>/login</c>, <c>/account</c>) or an
    /// operator-supplied target that passed <see cref="LocalReturnUrl.Validate"/>. It is still checked for browser-stable
    /// local-path shape (defense in depth; the reserved-surface rule is not applied because the host's own constants are
    /// auth surfaces), and anything else becomes the UI root. The optional query parameter is placed before any fragment,
    /// so a fragment-carrying return target survives (<c>/x#notes</c> → <c>/x?steppedUp=1#notes</c>).
    /// </summary>
    public static string Build(IConfiguration? configuration, string relativePath, string? queryKey = null, string? queryValue = null)
    {
        var path = LocalReturnUrl.IsLocalPath(relativePath) ? relativePath : "/";
        var url = ResolveBaseUrl(configuration) + path;
        if (string.IsNullOrEmpty(queryKey) || string.IsNullOrEmpty(queryValue))
            return url;

        var hash = url.IndexOf('#', StringComparison.Ordinal);
        var head = hash < 0 ? url : url[..hash];
        var fragment = hash < 0 ? "" : url[hash..];
        var separator = head.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{head}{separator}{queryKey}={Uri.EscapeDataString(queryValue)}{fragment}";
    }
}
