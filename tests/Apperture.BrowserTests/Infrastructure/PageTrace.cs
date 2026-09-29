using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>One HTTP response the page received (a navigation hop, a fetch/XHR, or a sub-resource).</summary>
public sealed record TracedResponse(
    string Method,
    string Url,
    int Status,
    string ResourceType,
    bool CarriesPlatformRequest = false,
    bool CarriesBackgroundMarker = false)
{
    public Uri Uri => new(Url);

    public override string ToString() =>
        $"{Method} {Url} -> {Status} ({ResourceType}) platform={CarriesPlatformRequest} background={CarriesBackgroundMarker}";
}

/// <summary>
/// Everything the browser did on one page, in order: main-frame URL changes (including SPA history
/// changes), every response (with redirect hops), console output and page errors. It is the browser-side
/// evidence the characterization tests assert on and quote.
/// </summary>
public sealed class PageTrace
{
    private readonly ConcurrentQueue<string> _urls = new();
    private readonly ConcurrentQueue<TracedResponse> _responses = new();
    private readonly ConcurrentQueue<string> _console = new();

    public PageTrace(IPage page)
    {
        Page = page;
        page.FrameNavigated += (_, frame) =>
        {
            if (frame.ParentFrame is null)
                _urls.Enqueue(frame.Url);
        };
        page.Response += (_, response) =>
        {
            var request = response.Request;
            var headers = request.Headers;
            _responses.Enqueue(new TracedResponse(
                request.Method,
                response.Url,
                response.Status,
                request.ResourceType,
                CarriesHeader(headers, "x-apperture-request"),
                CarriesHeader(headers, "x-apperture-background")));
        };
        page.Console += (_, message) => _console.Enqueue($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => _console.Enqueue($"[pageerror] {error}");
    }

    public IPage Page { get; }

    /// <summary>Main-frame URLs in the order the browser reported them.</summary>
    public IReadOnlyList<string> Urls => _urls.ToArray();

    public IReadOnlyList<TracedResponse> Responses => _responses.ToArray();

    public IReadOnlyList<string> Console => _console.ToArray();

    /// <summary>Document (navigation) responses only, i.e. the redirect chain the browser followed.</summary>
    public IReadOnlyList<TracedResponse> Documents => Responses.Where(r => r.ResourceType == "document").ToArray();

    private static bool CarriesHeader(IDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) && value.Length > 0;

    public IReadOnlyList<TracedResponse> ApiCalls(string pathContains) =>
        Responses.Where(r => r.ResourceType is "fetch" or "xhr" && r.Url.Contains(pathContains, StringComparison.Ordinal)).ToArray();

    public void Clear()
    {
        while (_urls.TryDequeue(out _))
        {
        }

        while (_responses.TryDequeue(out _))
        {
        }

        while (_console.TryDequeue(out _))
        {
        }
    }
}
