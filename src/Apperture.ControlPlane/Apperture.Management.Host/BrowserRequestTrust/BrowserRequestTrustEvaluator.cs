namespace Apperture.Management.Host;

internal enum BrowserRequestTrustRule
{
    None = 0,
    OriginMissing,
    OriginRejected,
    HeaderMissing,
    HeaderRejected,
    FetchMetadataCrossSite,
}

internal readonly record struct BrowserRequestTrustFacts(
    string Method,
    string Path,
    IReadOnlyList<string> Origins,
    IReadOnlyList<string> RequestHeaders,
    IReadOnlyList<string> FetchSites,
    bool EndpointExempt);

internal readonly record struct BrowserRequestTrustResult(bool Allowed, BrowserRequestTrustRule Rule)
{
    public static BrowserRequestTrustResult Allow { get; } = new(true, BrowserRequestTrustRule.None);

    public static BrowserRequestTrustResult Reject(BrowserRequestTrustRule rule) => new(false, rule);
}

/// <summary>
/// Pure classification for the browser trust boundary. The middleware supplies the facts; this type does not read the request.
/// </summary>
internal static class BrowserRequestTrustEvaluator
{
    public const string CrossSite = "cross-site";

    public static BrowserRequestTrustResult Evaluate(BrowserRequestTrustFacts facts, BrowserRequestTrustOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (IsOptions(facts.Method) || IsOidcCallback(facts.Path, options))
            return BrowserRequestTrustResult.Allow;

        if (!IsApi(facts.Path))
            return BrowserRequestTrustResult.Allow;

        if (IsSafeRead(facts.Method))
            return CrossSiteFetch(facts) ? BrowserRequestTrustResult.Reject(BrowserRequestTrustRule.FetchMetadataCrossSite) : BrowserRequestTrustResult.Allow;

        if (facts.EndpointExempt)
            return BrowserRequestTrustResult.Allow;

        if (!TrySingle(facts.Origins, out var originText))
            return BrowserRequestTrustResult.Reject(facts.Origins.Count == 0 ? BrowserRequestTrustRule.OriginMissing : BrowserRequestTrustRule.OriginRejected);

        if (!BrowserOrigin.TryParse(originText, out var origin) || !options.TrustedOrigins.Any(trusted => trusted.Equals(origin)))
            return BrowserRequestTrustResult.Reject(BrowserRequestTrustRule.OriginRejected);

        if (!TrySingle(facts.RequestHeaders, out var header))
            return BrowserRequestTrustResult.Reject(facts.RequestHeaders.Count == 0 ? BrowserRequestTrustRule.HeaderMissing : BrowserRequestTrustRule.HeaderRejected);

        if (!string.Equals(header, Apperture.Shared.Kernel.BrowserRequest.HeaderValue, StringComparison.Ordinal))
            return BrowserRequestTrustResult.Reject(BrowserRequestTrustRule.HeaderRejected);

        if (CrossSiteFetch(facts))
            return BrowserRequestTrustResult.Reject(BrowserRequestTrustRule.FetchMetadataCrossSite);

        return BrowserRequestTrustResult.Allow;
    }

    public static string RuleId(BrowserRequestTrustRule rule) => rule switch
    {
        BrowserRequestTrustRule.OriginMissing => "origin_missing",
        BrowserRequestTrustRule.OriginRejected => "origin_rejected",
        BrowserRequestTrustRule.HeaderMissing => "request_header_missing",
        BrowserRequestTrustRule.HeaderRejected => "request_header_rejected",
        BrowserRequestTrustRule.FetchMetadataCrossSite => "fetch_metadata_cross_site",
        _ => "none",
    };

    private static bool CrossSiteFetch(BrowserRequestTrustFacts facts)
    {
        if (facts.FetchSites.Count == 0)
            return false;

        if (facts.FetchSites.Count != 1)
            return true;

        return string.Equals(facts.FetchSites[0], CrossSite, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySingle(IReadOnlyList<string> values, out string value)
    {
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]) || values[0].Contains(',', StringComparison.Ordinal))
        {
            value = "";
            return false;
        }

        value = values[0];
        return true;
    }

    private static bool IsOptions(string method) =>
        string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeRead(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    internal static bool IsApi(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return path.Equals("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOidcCallback(string path, BrowserRequestTrustOptions options)
    {
        foreach (var candidate in options.OidcCallbackPaths)
        {
            if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
