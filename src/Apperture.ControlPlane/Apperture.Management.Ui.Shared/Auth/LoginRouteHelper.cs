namespace Apperture.Management.Ui.Shared.Auth;

public static class LoginRouteHelper
{
    public static readonly string[] PublicPaths = ["/login", "/login/local"];

    public static bool IsPublicPath(string absolutePath)
    {
        var path = absolutePath.TrimEnd('/') == "" ? "/" : absolutePath.TrimEnd('/');
        return PublicPaths.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase));
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

    public static string DefaultPostLoginPath => "/dashboard";

    /// <summary>Reads <c>returnUrl</c> from a URI query string (first occurrence).</summary>
    public static string? GetReturnUrlRaw(string absoluteUri)
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
            if (!name.Equals("returnUrl", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = segment[(eq + 1)..];
            return Uri.UnescapeDataString(value.ToString());
        }

        return null;
    }
}
