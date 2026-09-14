using System.Net;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A finding I2: <see cref="RunQueryFilter.ResourceContextId"/> (added to the client DTO in
/// Task 3) was never actually sent by <c>ManagementClient.BuildRunsQuery</c> -- a silent-wrong-data
/// trap where a caller could set the filter and get an unfiltered result while believing it was
/// scoped. The server-side runs endpoint's query parameter is named "resourceContextId"
/// (<c>RunsController</c>), NOT "contextId" like the Integrations/Connections/Schedules list
/// endpoints covered by <see cref="ManagementClientContextFilterTests"/> -- the two must not be
/// conflated.
/// </summary>
public sealed class ManagementClientRunsResourceContextFilterTests
{
    [Fact]
    public async Task GetRunsAsync_WithResourceContextIdFilter_AppendsResourceContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetRunsAsync(new RunQueryFilter(ResourceContextId: "canvas-id"));

        handler.LastUri!.ToString().Should().Contain("resourceContextId=canvas-id");
    }

    [Fact]
    public async Task GetRunsAsync_WithoutResourceContextIdFilter_SendsNoResourceContextIdQueryParam()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, "[]");
        var client = new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

        await client.GetRunsAsync(new RunQueryFilter());

        handler.LastUri!.ToString().Should().NotContain("resourceContextId");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public Uri? LastUri { get; private set; }

        public CaptureHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
