namespace Apperture.Management.Ui.Shared.Auth;

public static class LoginRouteHelper
{
    public static readonly string[] LoginChooserPaths = ["/login", "/login/local"];

    public static readonly string[] AccountActionPublicPaths =
    [
        "/account/verify-email",
        "/account/setup-password",
        "/account/reset-password",
        "/account/forgot-password",
        "/access-request"
    ];

    public static readonly string[] PublicPaths = [.. LoginChooserPaths, .. AccountActionPublicPaths];

    public static bool IsPublicPath(string absolutePath)
    {
        var path = NormalizeAbsolutePath(absolutePath);
        return PublicPaths.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAccessRequestPath(string absolutePath) =>
        string.Equals(NormalizeAbsolutePath(absolutePath), "/access-request", StringComparison.OrdinalIgnoreCase);

    public static bool IsLoginChooserPath(string absolutePath)
    {
        var path = NormalizeAbsolutePath(absolutePath);
        return LoginChooserPaths.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// App root only bounces into the platform (dashboard or login). Never mount MainLayout here.
    /// </summary>
    public static bool IsRootBootstrapPath(string absolutePath) =>
        NormalizeAbsolutePath(absolutePath) == "/";

    public static string NormalizeAbsolutePath(string absolutePath)
    {
        var path = absolutePath.TrimEnd('/');
        return path == "" ? "/" : path;
    }

    /// <summary>Returns a relative path safe for same-app navigation, or null if untrusted.</summary>
    public static string? NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;
        var t = returnUrl.Trim();
        if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal))
            return null;
        if (!t.StartsWith('/'))
            return null;
        return t;
    }

    public static string DefaultPostLoginPath => "/control/center";

    /// <summary>The <c>reason</c> value the expired-session recovery appends to the sign-in URL.</summary>
    public const string ExpiredReason = "expired";

    /// <summary>
    /// The one place that builds an automatic sign-in navigation target: <c>/login?returnUrl=&lt;escaped once&gt;</c>,
    /// plus <c>&amp;reason=</c> when the operator lost a session they had. <paramref name="pathAndQuery"/> is the current
    /// local path and query (the fragment is not captured today).
    /// </summary>
    public static string BuildLoginUrl(string pathAndQuery, string? reason = null)
    {
        var url = "/login?returnUrl=" + Uri.EscapeDataString(pathAndQuery);
        return string.IsNullOrEmpty(reason) ? url : url + "&reason=" + Uri.EscapeDataString(reason);
    }

    /// <summary>True when the sign-in URL says the operator arrived because their session expired.</summary>
    public static bool IsExpiredReason(string absoluteUri) =>
        string.Equals(GetQueryValueRaw(absoluteUri, "reason"), ExpiredReason, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads <c>returnUrl</c> from a URI query string (first occurrence).</summary>
    public static string? GetReturnUrlRaw(string absoluteUri) => GetQueryValueRaw(absoluteUri, "returnUrl");

    private static string? GetQueryValueRaw(string absoluteUri, string parameterName)
    {
        var q = new Uri(absoluteUri).Query;
        if (string.IsNullOrEmpty(q) || q == "?")
            return null;
        var s = q.AsSpan().TrimStart('?');
        while (!s.IsEmpty)
        {
            ReadOnlySpan<char> segment;
            var amp = s.IndexOf('&');
            if (amp < 0)
            {
                segment = s;
                s = ReadOnlySpan<char>.Empty;
            }
            else
            {
                segment = s[..amp];
                s = s[(amp + 1)..];
            }

            var eq = segment.IndexOf('=');
            if (eq <= 0)
                continue;
            var name = segment[..eq];
            if (!name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = segment[(eq + 1)..];
            return Uri.UnescapeDataString(value.ToString());
        }

        return null;
    }
}
