using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// What the Management.Host actually received, seen from the very first middleware. This is the
/// authoritative evidence for request-forgery baselines: it is the server's view of the browser's request,
/// not the test's opinion of it. Cookie and token <em>values</em> are never captured; only presence and
/// attribute names.
/// </summary>
public sealed record ObservedRequest(
    long Sequence,
    string Method,
    string Path,
    string? Query,
    int Status,
    string? Origin,
    string? SecFetchSite,
    string? SecFetchMode,
    string? SecFetchDest,
    string? Referer,
    string? ContentType,
    bool CookieHeaderPresent,
    bool SessionCookiePresent,
    IReadOnlyList<string> CookieNames,
    bool CustomHeaderPresent,
    string? PlatformRequestHeaderValue,
    bool BackgroundHeaderPresent,
    IReadOnlyList<string> SetCookieSummaries)
{
    /// <summary>True when the response re-issued the application session cookie (sliding renewal or sign-in).</summary>
    public bool SetSessionCookie => SetCookieSummaries.Any(s => s.StartsWith(RequestRecorder.SessionCookieName, StringComparison.Ordinal));

    public override string ToString() =>
        $"{Method} {Path}{(Query is { Length: > 0 } ? "?" + Query : "")} -> {Status} " +
        $"[origin={Origin ?? "-"} sfs={SecFetchSite ?? "-"} sfm={SecFetchMode ?? "-"} cookies=[{string.Join(",", CookieNames)}] setcookie=[{string.Join(" || ", SetCookieSummaries)}]]";
}

/// <summary>Thread-safe, append-only log of requests seen by the hosted Management.Host.</summary>
public sealed class RequestRecorder
{
    public const string SessionCookieName = ".AspNetCore.Cookies";

    /// <summary>Header the design gate proposes for the platform request header (SEC-1B1). Recorded, never required.</summary>
    public const string PlatformRequestHeader = "X-Apperture-Request";

    public const string BackgroundRequestHeader = "X-Apperture-Background";

    private readonly ConcurrentQueue<ObservedRequest> _requests = new();
    private long _sequence;

    public IReadOnlyList<ObservedRequest> Snapshot() => _requests.ToArray();

    public void Clear()
    {
        while (_requests.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ObservedRequest> Where(Func<ObservedRequest, bool> predicate) =>
        _requests.Where(predicate).ToArray();

    internal void Add(HttpContext context, int status)
    {
        var request = context.Request;
        var cookieHeader = request.Headers.Cookie.ToString();
        var setCookies = context.Response.Headers.SetCookie
            .Select(SummariseSetCookie)
            .ToArray();

        _requests.Enqueue(new ObservedRequest(
            Interlocked.Increment(ref _sequence),
            request.Method,
            request.Path.Value ?? "",
            request.QueryString.HasValue ? request.QueryString.Value!.TrimStart('?') : null,
            status,
            Header(request, "Origin"),
            Header(request, "Sec-Fetch-Site"),
            Header(request, "Sec-Fetch-Mode"),
            Header(request, "Sec-Fetch-Dest"),
            Header(request, "Referer"),
            request.ContentType,
            cookieHeader.Length > 0,
            cookieHeader.Contains(SessionCookieName, StringComparison.Ordinal),
            CookieNamesOf(cookieHeader),
            request.Headers.ContainsKey(PlatformRequestHeader),
            Header(request, PlatformRequestHeader),
            request.Headers.ContainsKey(BackgroundRequestHeader),
            setCookies!));
    }

    /// <summary>The names (never values) of the cookies a request carried.</summary>
    private static string[] CookieNamesOf(string cookieHeader) =>
        cookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            // OpenID Connect nonce/correlation cookie NAMES embed a long protected blob; keep the recognisable prefix only.
            .Select(name => name.Length > 40 ? name[..40] + "…" : name)
            .ToArray();

    private static string? Header(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) && value.Count > 0 ? value.ToString() : null;

    /// <summary>Keeps the cookie name and attributes; drops the value.</summary>
    public static string SummariseSetCookie(string? setCookie)
    {
        if (string.IsNullOrEmpty(setCookie))
            return "";

        var parts = setCookie.Split(';', StringSplitOptions.TrimEntries);
        var name = parts[0].Split('=', 2)[0];
        var attributes = parts.Skip(1)
            .Select(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) ? "expires=<set>" : a);
        return name + "=<redacted>; " + string.Join("; ", attributes);
    }
}

/// <summary>
/// Puts the recorder in front of the application's own pipeline. It only observes: it never alters,
/// short-circuits or answers a request, so the Management.Host behavior under test is unchanged.
/// </summary>
internal sealed class RequestRecorderStartupFilter(RequestRecorder recorder) : IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
        Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, pipeline) =>
            {
                try
                {
                    await pipeline().ConfigureAwait(false);
                }
                finally
                {
                    // Recorded after the pipeline has produced its answer (including short-circuits such as
                    // CORS preflights), so status and Set-Cookie headers are final.
                    recorder.Add(context, context.Response.StatusCode);
                }
            });

            next(app);
        };
}
