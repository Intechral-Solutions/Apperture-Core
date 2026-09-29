using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Serves attacker pages from two origins that the browser treats differently from the application:
/// <c>http://127.0.0.1:{port}</c> (cross-<em>site</em>) and <c>http://localhost:{port}</c> on another port
/// (same-site, cross-origin: the "sibling origin" of the design gate). Tests register the HTML each origin
/// should serve; the pages contain no exploit beyond what a hostile web page can do with ordinary
/// <c>&lt;form&gt;</c> / <c>fetch</c> APIs.
/// </summary>
public sealed class AttackerHost : IAsyncDisposable
{
    private readonly HarnessOrigins _origins;
    private readonly ConcurrentDictionary<string, string> _pages = new(StringComparer.Ordinal);
    private WebApplication? _app;

    public AttackerHost(HarnessOrigins origins) => _origins = origins;

    /// <summary>Registers <paramref name="html"/> at <paramref name="path"/> on both attacker origins.</summary>
    public void Serve(string path, string html) => _pages[path] = html;

    public void Clear() => _pages.Clear();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(_origins.CrossSiteAttacker, _origins.SameSiteAttacker);
        var app = builder.Build();
        // An explicit catch-all pattern: the pattern-less MapFallback carries a 'nonfile' constraint, so it
        // would 404 every '*.html' path.
        app.MapFallback("{*path}", context =>
        {
            if (!_pages.TryGetValue(context.Request.Path.Value ?? "/", out var html))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            return context.Response.WriteAsync(html);
        });

        await app.StartAsync().ConfigureAwait(false);
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
            return;

        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // Teardown must never mask a test failure.
        }

        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
    }
}
