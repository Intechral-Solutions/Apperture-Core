namespace Apperture.BrowserTests.Infrastructure;

/// <summary>Playwright engines. Chromium runs the full suite; the focused unsafe-request header probe runs on all three.</summary>
public enum BrowserEngine
{
    Chromium,
    Firefox,
    WebKit,
}

public static class BrowserEngines
{
    public static readonly BrowserEngine[] All = [BrowserEngine.Chromium, BrowserEngine.Firefox, BrowserEngine.WebKit];

    /// <summary>
    /// Engines that can complete an OpenID Connect handshake over the harness's plain-http localhost topology.
    /// The Playwright WebKit build for Windows (WinCairo) rejects the <c>Secure</c> OIDC correlation cookie on
    /// <c>http://localhost</c> ("correlation cookie not found"), so no OIDC flow can be measured there;
    /// Safari itself is a manual-smoke item.
    /// </summary>
    public static readonly BrowserEngine[] OidcCapable = [BrowserEngine.Chromium, BrowserEngine.Firefox];

    public static IEnumerable<object[]> OidcCapableEngines() => OidcCapable.Select(e => new object[] { e });

    /// <summary>xunit <c>MemberData</c> source for the three-engine tests.</summary>
    public static IEnumerable<object[]> AllEngines() => All.Select(e => new object[] { e });
}
