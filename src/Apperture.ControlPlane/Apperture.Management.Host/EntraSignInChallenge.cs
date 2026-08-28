using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;

namespace Apperture.Management.Host;

/// <summary>
/// Builds the post-authentication destination for a normal Microsoft sign-in challenge.
/// The OIDC handler defaults a missing <see cref="AuthenticationProperties.RedirectUri"/> to the
/// current request, which for <c>/signin</c> would send the browser back into another challenge.
/// </summary>
internal static class EntraSignInChallenge
{
    /// <summary>Canonical authenticated landing route in the management UI (same as local sign-in).</summary>
    public const string DefaultReturnPath = "/dashboard";

    public static AuthenticationProperties CreateProperties(IConfiguration configuration, string? returnUrl)
    {
        var properties = new AuthenticationProperties();
        properties.RedirectUri = ManagementUiRedirects.Build(configuration, ResolveReturnPath(returnUrl));
        return properties;
    }

    /// <summary>
    /// Replaces a missing, host-relative, or off-site redirect with the UI landing route so a
    /// successful callback can never bounce back to <c>/signin</c> or an external host.
    /// </summary>
    public static void EnsureSafePostAuthenticationRedirect(AuthenticationProperties properties, IConfiguration configuration)
    {
        if (IsSafeUiRedirect(properties.RedirectUri, configuration))
            return;

        properties.RedirectUri = ManagementUiRedirects.Build(configuration, DefaultReturnPath);
    }

    public static string ResolveReturnPath(string? returnUrl)
    {
        var path = ManagementUiRedirects.NormalizeRelativePath(returnUrl);
        if (path is null || IsReservedAuthPath(path))
            return DefaultReturnPath;

        return path;
    }

    public static bool IsSafeUiRedirect(string? redirectUri, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return false;

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
            return false;

        var uiBase = ManagementUiRedirects.ResolveBaseUrl(configuration);
        if (!Uri.TryCreate(uiBase.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;

        if (!string.Equals(uri.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            return false;

        return !IsReservedAuthPath(uri.AbsolutePath);
    }

    public static bool IsReservedAuthPath(string path)
    {
        var withoutQuery = path.Split('?', 2)[0];
        var trimmed = withoutQuery.TrimEnd('/');
        if (trimmed.Length == 0)
            trimmed = "/";

        return trimmed.Equals("/signin", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("/signin/link", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("/signin/step-up", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("/signout", StringComparison.OrdinalIgnoreCase);
    }
}
