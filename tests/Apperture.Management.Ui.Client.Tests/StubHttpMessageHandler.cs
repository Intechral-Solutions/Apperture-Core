using System.Net;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// Path-routed HTTP stand-in for the management API. Every request is recorded; a route answers with a status
/// or throws (a network failure). Lets the REAL <c>ManagementClient</c>, handlers and coordinator run against
/// exact status codes without a host or a browser.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public List<(HttpMethod Method, string Path)> Requests { get; } = new();

    public int Count(string path) => Requests.Count(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    public StubHttpMessageHandler Respond(string path, HttpStatusCode status, string body = "{}")
    {
        _routes[path] = _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
        return this;
    }

    public StubHttpMessageHandler Throw(string path, Exception exception)
    {
        _routes[path] = _ => throw exception;
        return this;
    }

    /// <summary>Answers with a response that completes only when the test releases <paramref name="gate"/>.</summary>
    public StubHttpMessageHandler RespondWhen(string path, TaskCompletionSource gate, HttpStatusCode status, string body = "{}")
    {
        _routes[path] = async _ =>
        {
            await gate.Task;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Path relative to the API base (the test base address is https://api.example/api/).
        var path = request.RequestUri!.AbsolutePath;
        const string marker = "/api/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var relative = index >= 0 ? path[(index + marker.Length)..] : path.TrimStart('/');
        Requests.Add((request.Method, relative));

        return _routes.TryGetValue(relative, out var route)
            ? route(request)
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
