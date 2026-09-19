using System.Net;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A (A1): GetIntegrationsAsync/GetConnectionsAsync/GetSchedulesAsync accept an optional contextId that,
/// when supplied, is appended as a query-string parameter; when omitted, the request is unchanged from today
/// so existing callers keep receiving the same global list.
/// </summary>
public sealed class ManagementClientContextFilterTests
{
    [Fact]
    public async Task GetIntegrationsAsync_WithContextId_AppendsContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetIntegrationsAsync(contextId: "canvas-id");

        handler.LastUri!.ToString().Should().Contain("contextId=canvas-id");
    }

    [Fact]
    public async Task GetIntegrationsAsync_WithoutContextId_SendsNoContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetIntegrationsAsync();

        handler.LastUri!.ToString().Should().NotContain("contextId");
    }

    [Fact]
    public async Task GetIntegrationsAsync_WithModuleId_AppendsModuleIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetIntegrationsAsync(moduleId: "Sample.Mod");

        handler.LastUri!.ToString().Should().Contain("moduleId=Sample.Mod");
    }

    [Fact]
    public async Task GetIntegrationsAsync_WithContextIdAndModuleId_AppendsBothQueryParams()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetIntegrationsAsync(contextId: "canvas-id", moduleId: "Sample.Mod");

        handler.LastUri!.ToString().Should().Contain("contextId=canvas-id").And.Contain("moduleId=Sample.Mod");
    }

    [Fact]
    public async Task GetIntegrationsAsync_WithoutModuleId_SendsNoModuleIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetIntegrationsAsync();

        handler.LastUri!.ToString().Should().NotContain("moduleId");
    }

    [Fact]
    public async Task GetConnectionsAsync_WithContextId_AppendsContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetConnectionsAsync(contextId: "canvas-id");

        handler.LastUri!.ToString().Should().Contain("contextId=canvas-id");
    }

    [Fact]
    public async Task GetConnectionsAsync_WithoutContextId_SendsNoContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetConnectionsAsync();

        handler.LastUri!.ToString().Should().NotContain("contextId");
    }

    [Fact]
    public async Task GetSchedulesAsync_WithContextId_AppendsContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetSchedulesAsync(contextId: "canvas-id");

        handler.LastUri!.ToString().Should().Contain("contextId=canvas-id");
    }

    [Fact]
    public async Task GetSchedulesAsync_WithoutContextId_SendsNoContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetSchedulesAsync();

        handler.LastUri!.ToString().Should().NotContain("contextId");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        public CaptureHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
