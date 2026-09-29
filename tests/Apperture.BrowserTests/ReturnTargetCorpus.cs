namespace Apperture.BrowserTests;

/// <summary>
/// One <c>returnUrl</c> candidate. <see cref="Value"/> is the string the client sees AFTER the single
/// percent-decode it performs on the query value (the tests encode it exactly once when building the
/// <c>/login?returnUrl=…</c> URL). Control characters are written as C# escapes on purpose.
/// </summary>
public sealed record ReturnTargetCase(string Id, string Value, string Note);

/// <summary>
/// The SEC-1.0 return-target corpus: hostile candidates whose fate in a real browser is measured, and
/// legitimate deep links that any future <c>LocalReturnUrl</c> policy must keep accepting.
/// </summary>
public static class ReturnTargetCorpus
{
    public const string Host = "evil.example";

    /// <summary>Candidates an attacker could put in a link. What the browser really does with each is what the tests measure.</summary>
    public static readonly ReturnTargetCase[] Hostile =
    [
        new("abs-https", "https://evil.example/x", "absolute external URL"),
        new("abs-http", "http://evil.example", "absolute external URL, http"),
        new("scheme-relative", "//evil.example", "scheme-relative authority"),
        new("scheme-relative-path", "//evil.example/deep/path", "scheme-relative with path"),
        new("slash-backslash", "/\\evil.example", "F5: slash then backslash"),
        new("slash-backslash-x2", "/\\\\evil.example", "slash then two backslashes"),
        new("slash-backslash-slash", "/\\/evil.example", "slash, backslash, slash"),
        new("backslash-slash", "\\/evil.example", "leading backslash then slash (no leading '/')"),
        new("backslash-backslash", "\\\\evil.example", "two leading backslashes (no leading '/')"),
        new("slash-backslash-at", "/\\@evil.example", "backslash before an '@' userinfo trick"),
        new("leading-space-slash-backslash", " /\\evil.example", "leading space is trimmed by the validator"),
        new("tab-in-authority", "/\t/evil.example", "TAB between the slashes: browsers strip it"),
        new("tab-before-backslash", "/\t\\evil.example", "TAB then backslash"),
        new("crlf-in-authority", "/\r\n/evil.example", "CR LF between the slashes: browsers strip them"),
        new("lf-in-authority", "/\n/evil.example", "LF between the slashes"),
        new("encoded-backslash-literal", "/%5Cevil.example", "literal '%5C' text after the single decode"),
        new("encoded-slash-slash-literal", "/%2F%2Fevil.example", "literal '%2F%2F' text after the single decode"),
        new("encoded-slash-lower-literal", "/%2fevil.example", "literal '%2f' text after the single decode"),
        new("double-encoded-backslash", "/%255Cevil.example", "literal '%255C' text after the single decode"),
        new("encoded-dotdot", "/%2E%2E/%2E%2E/evil.example", "literal encoded dot segments"),
        new("dot-segment-double-slash", "/.//evil.example", "dot segment before an empty segment"),
        new("javascript-scheme", "javascript:alert(1)", "script URL"),
        new("data-scheme", "data:text/html,<b>x</b>", "data URL"),
        new("bare-host", "evil.example", "no leading slash"),
        new("nested-login-backslash", "/login?returnUrl=%2F%5Cevil.example", "second hop: /login whose own returnUrl is the backslash form"),
        new("nested-login-scheme-relative", "/login?returnUrl=%2F%2Fevil.example", "second hop: /login whose own returnUrl is scheme-relative"),
    ];

    /// <summary>Auth-surface and loop candidates: on-origin, but they must never be a post-login destination.</summary>
    public static readonly ReturnTargetCase[] AuthSurfaces =
    [
        new("login", "/login", "the sign-in page itself"),
        new("login-local", "/login/local", "the local sign-in page"),
        new("login-self-return", "/login?returnUrl=%2Flogin", "a login route returning to login"),
        new("signin", "/signin", "the host sign-in path, resolved against the UI origin"),
        new("signout", "/signout", "the host sign-out path, resolved against the UI origin"),
        new("signin-oidc", "/signin-oidc", "the OIDC callback path, resolved against the UI origin"),
        new("access-request", "/access-request", "a public account route"),
    ];

    /// <summary>
    /// Legitimate deep links. <see cref="ReturnTargetCase.Value"/> is what must survive a sign-in;
    /// the expected browser location is the same string on the UI origin.
    /// </summary>
    public static readonly ReturnTargetCase[] Legitimate =
    [
        new("control-center", "/control/center", "default landing route"),
        new("connection-detail", "/connections/0f8fad5bd9cb469fa16570867728950e", "detail route with a 32-hex id"),
        new("connection-query", "/connections/0f8fad5bd9cb469fa16570867728950e?foo=bar", "detail route with a query"),
        new("path-query-fragment", "/connections/0f8fad5bd9cb469fa16570867728950e?foo=bar#notes", "path + query + fragment"),
        new("component-dotted", "/components/connector/Apperture.Platform.Sql", "connector component with a dotted native id"),
        new("notification-definition-dotted", "/config/notifications/definitions/platform.schedule.expected_run_missing", "built-in Notification Definition dotted id"),
        new("module-dotted", "/modules/Sample.HelloWorld", "Module dotted id"),
        new("query-percent-encoded-data", "/runs?status=failed&q=a%20b%2Fc", "percent-encoded user data inside the query"),
        new("query-encoded-return", "/connections?next=%2Fcontrol%2Fcenter", "an encoded slash inside a query value is data"),
        new("query-unicode", "/runs?q=%E2%9C%93", "percent-encoded Unicode in the query"),
        new("path-unicode", "/connections/caf%C3%A9", "percent-encoded Unicode in a path segment"),
        new("path-encoded-slash", "/connections/a%2Fb", "encoded slash inside one path segment (id containing '/')"),
        new("path-encoded-dot", "/connections/a%2Eb", "encoded dot inside a path segment"),
        new("runs-status", "/runs?status=failed", "list route with a filter"),
        new("trailing-slash", "/connections/", "trailing slash"),
    ];

    public static IEnumerable<ReturnTargetCase> All => Hostile.Concat(AuthSurfaces).Concat(Legitimate);

    public static ReturnTargetCase ById(string id) => All.Single(c => c.Id == id);

    public static IEnumerable<object[]> Ids(IEnumerable<ReturnTargetCase> cases) => cases.Select(c => new object[] { c.Id });

    public static IEnumerable<object[]> HostileIds() => Ids(Hostile);

    public static IEnumerable<object[]> AuthSurfaceIds() => Ids(AuthSurfaces);

    public static IEnumerable<object[]> LegitimateIds() => Ids(Legitimate);
}
