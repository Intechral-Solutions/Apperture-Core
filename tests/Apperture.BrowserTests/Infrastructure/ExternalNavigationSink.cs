using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Catches every top-level navigation that leaves the harness's own hosts (<c>localhost</c>,
/// <c>127.0.0.1</c>) and answers it locally with a blank page, so "the browser was sent to evil.example" is
/// observed without any packet leaving the machine (the <c>.example</c> TLD is reserved and never resolves).
/// Sub-resource requests to other hosts (web fonts, etc.) are aborted so the tests stay hermetic.
/// </summary>
public sealed class ExternalNavigationSink
{
    private readonly ConcurrentQueue<string> _navigations = new();

    public IReadOnlyList<string> Navigations => _navigations.ToArray();

    public bool Escaped => !_navigations.IsEmpty;

    public string? First => _navigations.TryPeek(out var url) ? url : null;

    public static async Task<ExternalNavigationSink> InstallAsync(IBrowserContext context)
    {
        var sink = new ExternalNavigationSink();
        await context.RouteAsync(IsOffHarness, async route =>
        {
            // 'document' rather than IsNavigationRequest: the latter is not reliably set on redirect hops.
            if (route.Request.ResourceType == "document")
            {
                sink._navigations.Enqueue(route.Request.Url);
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html",
                    Body = "<!doctype html><title>external</title><p>external page</p>",
                }).ConfigureAwait(false);
                return;
            }

            await route.AbortAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
        return sink;
    }

    private static bool IsOffHarness(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;

        return uri.Host is not ("localhost" or "127.0.0.1");
    }
}
