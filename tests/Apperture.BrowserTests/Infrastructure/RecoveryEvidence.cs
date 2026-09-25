using Microsoft.Playwright;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// Reads session-recovery evidence (SEC-1A1) out of a <see cref="PageTrace"/>: how many authoritative
/// <c>auth/me</c> re-checks a tab made, and how many times it navigated to sign-in.
/// </summary>
public static class RecoveryEvidence
{
    /// <summary>Document loads of the sign-in page (<c>/login</c> exactly) in the whole trace.</summary>
    public static IReadOnlyList<TracedResponse> SignInDocuments(this PageTrace trace) =>
        trace.Documents.Where(d => d.Uri.AbsolutePath == "/login").ToArray();

    /// <summary>
    /// <c>auth/me</c> fetches made from <paramref name="fromIndex"/> up to (not including) the first document load of
    /// <c>/login</c>: the tab's own recovery re-checks, not the new sign-in document's boot check.
    /// </summary>
    public static IReadOnlyList<TracedResponse> RecoveryProbes(this PageTrace trace, int fromIndex)
    {
        var slice = trace.Responses.Skip(fromIndex).ToArray();
        var signIn = Array.FindIndex(slice, r => r.ResourceType == "document" && r.Uri.AbsolutePath == "/login");
        var beforeSignIn = signIn < 0 ? slice : slice[..signIn];
        return beforeSignIn
            .Where(r => r.ResourceType is "fetch" or "xhr" && r.Url.Contains("/api/auth/me", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>API responses from <paramref name="fromIndex"/> on whose URL contains <paramref name="pathContains"/> (any method).</summary>
    public static IReadOnlyList<TracedResponse> ApiSince(this PageTrace trace, int fromIndex, string pathContains) =>
        trace.Responses.Skip(fromIndex)
            .Where(r => r.ResourceType is "fetch" or "xhr" && r.Url.Contains(pathContains, StringComparison.Ordinal))
            .ToArray();
}

/// <summary>
/// Samples the visible text of a page every few milliseconds and remembers whether a raw error ever appeared
/// (SEC-1A1: the operator must not see a flash of "401 Unauthorized" before recovery takes over).
/// </summary>
public sealed class RawErrorSampler : IAsyncDisposable
{
    private static readonly string[] DefaultNeedles = ["Unauthorized", "net_http_message"];

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    /// <param name="needles">Text whose appearance is recorded; defaults to the raw 401 error text.</param>
    public RawErrorSampler(IPage page, params string[] needles)
    {
        var watched = needles.Length > 0 ? needles : DefaultNeedles;
        _loop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var text = await page.VisibleTextAsync().ConfigureAwait(false);
                    if (watched.Any(n => text.Contains(n, StringComparison.Ordinal)))
                    {
                        Saw = true;
                        FirstSeenText ??= text[..Math.Min(text.Length, 300)];
                    }
                }
                catch (PlaywrightException)
                {
                    // Navigating: the execution context is gone for a moment.
                }

                try
                {
                    await Task.Delay(15, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    public bool Saw { get; private set; }

    public string? FirstSeenText { get; private set; }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}
