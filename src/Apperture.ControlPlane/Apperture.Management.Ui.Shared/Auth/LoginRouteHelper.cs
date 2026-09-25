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

    /// <summary>Where sign-in lands when there is no valid return target. The return-target rules themselves live in <see cref="LocalReturnUrl"/>.</summary>
    public static string DefaultPostLoginPath => LocalReturnUrl.Fallback;

    /// <summary>The <c>reason</c> value the expired-session recovery appends to the sign-in URL.</summary>
    public const string ExpiredReason = "expired";

    /// <summary>
    /// The one place that builds an automatic sign-in navigation target: <c>/login?returnUrl=&lt;escaped once&gt;</c>,
    /// plus <c>&amp;reason=</c> when the operator lost a session they had. <paramref name="localTarget"/> is the current
    /// local reference (<c>path?query#fragment</c>, see <see cref="LocalReturnUrl.FromAbsoluteUri"/>). A target the shared
    /// policy refuses (or none) is left out: sign-in then lands on the default route.
    /// </summary>
    public static string BuildLoginUrl(string? localTarget, string? reason = null)
    {
        var parts = new List<string>(2);
        if (LocalReturnUrl.Validate(localTarget) is { } safe)
            parts.Add("returnUrl=" + Uri.EscapeDataString(safe));
        if (!string.IsNullOrEmpty(reason))
            parts.Add("reason=" + Uri.EscapeDataString(reason));

        return parts.Count == 0 ? "/login" : "/login?" + string.Join('&', parts);
    }

    /// <summary>True when the sign-in URL says the operator arrived because their session expired.</summary>
    public static bool IsExpiredReason(string absoluteUri) =>
        string.Equals(GetQueryValueRaw(absoluteUri, "reason"), ExpiredReason, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <c>returnUrl</c> from a URI query string (first occurrence). The value has been percent-decoded exactly once and
    /// is NOT validated: hand it to <see cref="LocalReturnUrl"/> before use, or use <see cref="ReadReturnTarget"/>.
    /// </summary>
    public static string? GetReturnUrlRaw(string absoluteUri) => GetQueryValueRaw(absoluteUri, "returnUrl");

    /// <summary>The validated return target carried by a sign-in URL, or null when it has none or it is refused.</summary>
    public static string? ReadReturnTarget(string absoluteUri) => LocalReturnUrl.Validate(GetReturnUrlRaw(absoluteUri));

    /// <summary>
    /// True when the sign-in URL was built by the application's own hand-off (a <c>returnUrl</c> or <c>reason</c> is present),
    /// as opposed to a bare visit. Presence only: the values are not trusted or interpreted here.
    /// </summary>
    public static bool ArrivedFromApp(string absoluteUri) =>
        GetQueryValueRaw(absoluteUri, "returnUrl") is not null || GetQueryValueRaw(absoluteUri, "reason") is not null;

    /// <summary>The query <c>returnUrl=&lt;escaped once&gt;</c> for a link between the sign-in pages, or empty for no valid target.</summary>
    public static string BuildReturnQuery(string? validatedTarget) =>
        LocalReturnUrl.Validate(validatedTarget) is { } safe ? "returnUrl=" + Uri.EscapeDataString(safe) : "";

    /// <summary>
    /// The operator-facing meaning of the <c>error</c> code the host puts on <c>/login</c>. Only fixed codes are interpreted; an
    /// unknown or empty code is a generic sign-in failure. The code is never shown, and no exception text travels in the URL.
    /// </summary>
    public static LoginErrorKind ReadLoginError(string absoluteUri)
    {
        var code = GetQueryValueRaw(absoluteUri, "error");
        if (code is null)
            return LoginErrorKind.None;

        if (string.Equals(code, LoginErrorCodes.Cancelled, StringComparison.OrdinalIgnoreCase))
            return LoginErrorKind.Cancelled;
        if (string.Equals(code, LoginErrorCodes.AccountNotLinked, StringComparison.OrdinalIgnoreCase))
            return LoginErrorKind.AccountNotLinked;

        return LoginErrorKind.SignInFailed;
    }

    /// <summary>The value of a query parameter (first occurrence, percent-decoded once), or null when the parameter is absent.</summary>
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
            if (eq == 0)
                continue;

            // "?error" (no '=') is the parameter present with an empty value.
            var name = eq < 0 ? segment : segment[..eq];
            if (!name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                continue;
            return eq < 0 ? "" : Uri.UnescapeDataString(segment[(eq + 1)..].ToString());
        }

        return null;
    }
}

/// <summary>The fixed <c>error</c> codes the host puts on <c>/login</c> after a Microsoft sign-in that did not produce a session.</summary>
public static class LoginErrorCodes
{
    /// <summary>The operator (or the provider on their behalf) declined: <c>access_denied</c>.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Any other remote failure: a provider error, an invalid or replayed callback, a tampered state.</summary>
    public const string SignInFailed = "oidc";

    /// <summary>The Microsoft identity is not linked to an Apperture user.</summary>
    public const string AccountNotLinked = "account_not_linked";
}

public enum LoginErrorKind
{
    /// <summary>The sign-in URL carries no <c>error</c> parameter.</summary>
    None,

    Cancelled,

    AccountNotLinked,

    /// <summary>Any other code, including one this build does not know.</summary>
    SignInFailed,
}
