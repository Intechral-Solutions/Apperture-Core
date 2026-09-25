using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Apperture.BrowserTests.Infrastructure;

public static class TestSupport
{
    /// <summary>Polls <paramref name="condition"/> until it holds; fails with <paramref name="what"/> otherwise.</summary>
    public static async Task EventuallyAsync(Func<bool> condition, string what, TimeSpan? timeout = null, Func<string>? diagnostics = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for: {what}{(diagnostics is null ? "" : Environment.NewLine + diagnostics())}");
    }

    /// <summary>One-line dump of where a page is and how it got there, for timeout messages.</summary>
    public static string Describe(this PageTrace trace, RequestRecorder? recorder = null) =>
        $"page url: {trace.Page.Url}{Environment.NewLine}urls: {string.Join(" -> ", trace.Urls.TakeLast(8))}{Environment.NewLine}" +
        $"documents: {string.Join(" | ", trace.Documents.TakeLast(8))}{Environment.NewLine}" +
        $"api calls: {string.Join(" | ", trace.Responses.Where(r => r.ResourceType is "fetch" or "xhr" && r.Url.Contains("/api/", StringComparison.Ordinal)).TakeLast(8))}" +
        (recorder is null ? "" : $"{Environment.NewLine}host saw: {string.Join(" | ", recorder.Snapshot().TakeLast(10))}");

    /// <summary>Client-side (SPA) navigation through the Blazor router, with no document load.</summary>
    public static Task NavigateSpaAsync(this IPage page, string relative) =>
        page.EvaluateAsync("path => Blazor.navigateTo(path)", relative);

    /// <summary>Waits until <paramref name="text"/> appears in the rendered document.</summary>
    public static async Task WaitForTextAsync(this IPage page, string text, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        string last = "";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                last = await page.VisibleTextAsync().ConfigureAwait(false);
                if (last.Contains(text, StringComparison.Ordinal))
                    return;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Execution context was destroyed", StringComparison.Ordinal)
                                                 || ex.Message.Contains("navigat", StringComparison.OrdinalIgnoreCase))
            {
                // The page navigated under us (sign-in redirects, client URL rewrites); look again.
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for text '{text}'. Visible text was: {last[..Math.Min(last.Length, 300)]}");
    }

    /// <summary>Waits until the protected shell (navigation rail) has mounted.</summary>
    public static Task WaitForShellAsync(this IPage page) => page.WaitForTextAsync("Configuration");

    /// <summary>Visible text of the whole document, whitespace-collapsed.</summary>
    public static async Task<string> VisibleTextAsync(this IPage page)
    {
        var text = await page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''").ConfigureAwait(false);
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static void Line(this ITestOutputHelper output, string category, string message) =>
        output.WriteLine($"[{category}] {message}");
}
