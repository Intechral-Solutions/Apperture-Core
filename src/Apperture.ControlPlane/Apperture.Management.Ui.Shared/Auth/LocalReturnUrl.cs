namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>Why <see cref="LocalReturnUrl.Evaluate"/> accepted or refused a candidate. Diagnostic only; callers act on valid / not valid.</summary>
public enum LocalReturnUrlVerdict
{
    Valid,
    Empty,
    TooLong,

    /// <summary>A character outside printable ASCII (controls, whitespace, non-ASCII), or a backslash.</summary>
    IllegalCharacter,

    NotPathAbsolute,

    /// <summary>The first path segment is not a plain route name: an authority-shaped start (<c>//</c>), or any encoding, dot or punctuation there.</summary>
    AuthorityShapedStart,

    /// <summary>The path is not already in the form a browser would normalize it to (empty or dot segments).</summary>
    NonCanonicalPath,

    /// <summary>A sign-in, sign-out, callback or access-request surface. Returning there can start a loop.</summary>
    ReservedAuthPath,

    /// <summary>Supplementary check: resolving against a fixed sentinel origin did not stay on that origin.</summary>
    ResolvesOffOrigin,
}

/// <summary>
/// The one authoritative policy for a post-sign-in return target: a local, path-absolute reference on the Apperture UI
/// origin, as it would appear in an <c>href</c> (<c>/connections/abc?x=1#notes</c>). Used by the WebAssembly client
/// and by Management.Host; there is no second validator (a source guard pins that).
/// <para>
/// <b>Representation contract.</b> The value handed to this policy is the <c>returnUrl</c> query value after exactly ONE
/// percent-decode (the client's query reader, or ASP.NET's query binder). It is therefore still a URL reference: any
/// <c>%XX</c> left in it is data or an encoded separator inside that reference, and this policy never decodes it again.
/// The accepted string is emitted verbatim: <c>Uri.EscapeDataString</c> once into a query value, or appended to the UI
/// origin for a redirect. Nothing is trimmed, rewritten or "repaired": a candidate is either accepted unchanged or
/// refused, and the caller falls back to <see cref="Fallback"/>.
/// </para>
/// <para>
/// <b>Why string rules and not <c>Uri</c>.</b> Browsers parse a navigation target with the WHATWG URL algorithm: they
/// treat <c>\</c> as <c>/</c>, strip TAB/CR/LF anywhere and trim leading control characters and spaces, so <c>/\evil.example</c>
/// leaves the origin in a real browser while .NET's <see cref="Uri"/> resolves it on-origin (SEC-1.0 browser evidence,
/// design gate §38.2). So the policy admits only a narrow, browser-stable form and uses <see cref="Uri"/> solely as a
/// supplementary check.
/// </para>
/// <para>
/// <b>Percent-encoding.</b> SEC-1.0 proved browsers keep <c>%2F</c>, <c>%5C</c> and <c>%2E</c> text inside a path segment or
/// query and never turn it into a separator or authority, and that legitimate routes carry them (<c>/connections/a%2Fb</c>,
/// <c>?q=a%20b%2Fc</c>). They are therefore NOT rejected merely for being encoded. Encoding is refused in exactly one place,
/// the first path segment (the route area), where no legitimate route has any: that is the only position a lazy second
/// decoder could turn into an authority.
/// </para>
/// </summary>
public static class LocalReturnUrl
{
    /// <summary>Where sign-in lands when there is no valid return target (the canonical authenticated landing route).</summary>
    public const string Fallback = "/control/center";

    public const int MaxLength = 2048;

    private static readonly Uri SentinelOrigin = new("http://apperture.invalid/");

    /// <summary>
    /// First path segments (case-insensitive) that are authentication surfaces. A return target under one of them can re-enter
    /// sign-in (a nested <c>returnUrl</c> chain, a challenge, a callback) or is meaningless on the UI origin. Mirrors the host's
    /// <c>/signin</c>, <c>/signin/link</c>, <c>/signin/step-up</c>, <c>/signin-oidc</c>, <c>/signout</c>, <c>/signout-callback-oidc</c>
    /// and <c>/signout-oidc</c>, and the UI's <c>/login</c>, <c>/login/local</c> and <c>/access-request</c>.
    /// </summary>
    private static readonly HashSet<string> ReservedFirstSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "login",
        "access-request",
        "signin",
        "signin-oidc",
        "signout",
        "signout-callback-oidc",
        "signout-oidc",
    };

    /// <summary>The verdict for a candidate. <see cref="LocalReturnUrlVerdict.Valid"/> means <see cref="Validate"/> returns it unchanged.</summary>
    public static LocalReturnUrlVerdict Evaluate(string? candidate)
    {
        var structural = EvaluateStructure(candidate);
        if (structural != LocalReturnUrlVerdict.Valid)
            return structural;

        return IsReservedAuthPath(candidate!) ? LocalReturnUrlVerdict.ReservedAuthPath : LocalReturnUrlVerdict.Valid;
    }

    public static bool IsValid(string? candidate) => Evaluate(candidate) == LocalReturnUrlVerdict.Valid;

    /// <summary>The candidate itself when it is a valid return target, otherwise null. Never a modified copy.</summary>
    public static string? Validate(string? candidate) => IsValid(candidate) ? candidate : null;

    /// <summary>The candidate when valid, otherwise <see cref="Fallback"/>.</summary>
    public static string ResolveOrFallback(string? candidate) => Validate(candidate) ?? Fallback;

    /// <summary>
    /// True when the value is a well-formed local path reference, WITHOUT the reserved-surface rule. For trusted, host-built
    /// paths (<c>/login</c>, <c>/account</c>, …) that are legitimately auth surfaces but must still be browser-stable.
    /// Never use it to accept operator-supplied input: that is <see cref="Validate"/>.
    /// </summary>
    public static bool IsLocalPath(string? candidate) => EvaluateStructure(candidate) == LocalReturnUrlVerdict.Valid;

    /// <summary>
    /// True when the path (a query or fragment, if present, is ignored) is one of the authentication surfaces. Also used by the host
    /// for an absolute URL's path, so it takes any <c>/…</c> path, not only a whole return target.
    /// </summary>
    public static bool IsReservedAuthPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            return false;

        var end = path.IndexOfAny(['?', '#']);
        var onlyPath = end < 0 ? path : path[..end];
        var firstEnd = onlyPath.IndexOf('/', 1);
        var first = firstEnd < 0 ? onlyPath[1..] : onlyPath[1..firstEnd];
        return ReservedFirstSegments.Contains(first);
    }

    /// <summary>
    /// The local reference (<c>path?query#fragment</c>, exactly as the browser serialized it) of an absolute URI such as
    /// <c>NavigationManager.Uri</c>, or null when it has no scheme and authority. Not validated: pass the result to
    /// <see cref="Validate"/> or <c>LoginRouteHelper.BuildLoginUrl</c>. Taken from the string, not from <see cref="Uri"/>,
    /// so .NET canonicalization cannot change what the browser has.
    /// </summary>
    public static string? FromAbsoluteUri(string? absoluteUri)
    {
        if (string.IsNullOrEmpty(absoluteUri))
            return null;

        var schemeEnd = absoluteUri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return null;

        var authorityStart = schemeEnd + 3;
        var localStart = absoluteUri.AsSpan(authorityStart).IndexOfAny('/', '?', '#');
        if (localStart < 0)
            return "/";

        var local = absoluteUri[(authorityStart + localStart)..];
        return local[0] == '/' ? local : "/" + local;
    }

    private static LocalReturnUrlVerdict EvaluateStructure(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return LocalReturnUrlVerdict.Empty;

        if (candidate.Length > MaxLength)
            return LocalReturnUrlVerdict.TooLong;

        // Printable ASCII only, and no backslash. That refuses controls (TAB/CR/LF, which browsers silently strip),
        // spaces (a leading one is trimmed), DEL, every non-ASCII character, and the backslash browsers read as '/'.
        foreach (var c in candidate)
        {
            if (c is < '!' or > '~' || c == '\\')
                return LocalReturnUrlVerdict.IllegalCharacter;
        }

        if (candidate[0] != '/')
            return LocalReturnUrlVerdict.NotPathAbsolute;

        var pathEnd = candidate.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? candidate : candidate[..pathEnd];

        if (path.Length > 1)
        {
            var segments = path[1..].Split('/');

            // The first segment names the UI area (control, connections, runs, …). A plain route name only: an empty one is a
            // "//" authority-shaped start; encoding, dots or punctuation have no legitimate use there.
            if (!IsPlainRouteName(segments[0]))
                return LocalReturnUrlVerdict.AuthorityShapedStart;

            // Already in normalized form: a browser must not be able to rewrite the path (dot segments, empty segments)
            // into something other than what was validated. Only a single trailing slash (an empty last segment) is allowed.
            for (var i = 1; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0 && i != segments.Length - 1)
                    return LocalReturnUrlVerdict.NonCanonicalPath;

                if (IsDotSegment(segment))
                    return LocalReturnUrlVerdict.NonCanonicalPath;
            }
        }

        // Supplementary only (never the deciding check): the reference must still resolve inside the sentinel origin.
        if (!Uri.TryCreate(SentinelOrigin, candidate, out var resolved)
            || resolved.Scheme != SentinelOrigin.Scheme
            || !string.Equals(resolved.Authority, SentinelOrigin.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return LocalReturnUrlVerdict.ResolvesOffOrigin;
        }

        return LocalReturnUrlVerdict.Valid;
    }

    private static bool IsPlainRouteName(string segment)
    {
        if (segment.Length == 0)
            return false;

        foreach (var c in segment)
        {
            if (!(c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9' || c is '-' or '_'))
                return false;
        }

        return true;
    }

    // "." and ".." are dot segments in a browser, and so are their percent-encoded spellings (%2e).
    private static bool IsDotSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 6)
            return false;

        var normalized = segment.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase);
        return normalized is "." or "..";
    }
}
