using Microsoft.Extensions.Configuration;

namespace Apperture.Management.Host;

/// <summary>
/// Builds absolute UI URLs for browser redirects out of the OIDC callback. The management UI is a
/// separate origin from this host, so the base URL comes from configuration with a development default.
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

    /// <summary>Only same-site relative paths are accepted, so a crafted return url cannot bounce elsewhere.</summary>
    public static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            return null;
        return trimmed.StartsWith('/') ? trimmed : null;
    }

    public static string Build(IConfiguration? configuration, string relativePath, string? queryKey = null, string? queryValue = null)
    {
        var url = ResolveBaseUrl(configuration) + (NormalizeRelativePath(relativePath) ?? "/");
        if (string.IsNullOrEmpty(queryKey) || string.IsNullOrEmpty(queryValue))
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{queryKey}={Uri.EscapeDataString(queryValue)}";
    }
}
