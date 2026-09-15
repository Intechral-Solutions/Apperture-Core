using System.Net;
using System.Net.Http.Headers;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Regression coverage for a defect found during CTX-3A smoke testing: Activate/Deactivate/Retire on
/// <see cref="ManagementClient"/> used the bare <c>HttpResponseMessage.EnsureSuccessStatusCode()</c>
/// instead of the established <c>EnsureSuccessOrThrowAsync</c> convention (used elsewhere in this class),
/// so a 409 Conflict from the archived-Application reactivation guard surfaced to the operator as the
/// raw, unlocalized ".NET resource-key" text instead of the server's human-readable domain message.
/// </summary>
public sealed class ManagementClientIntegrationLifecycleTests
{
    [Fact]
    public async Task ActivateIntegrationAsync_On409ArchivedApplication_ThrowsWithServerDomainMessage()
    {
        const string domainMessage = "Test App is archived. Unarchive it first before activating this integration.";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, $$"""{ "error": "{{domainMessage}}" }"""));
        var client = CreateClient(handler);

        var act = () => client.ActivateIntegrationAsync("integration-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Which.Message.Should().Be(domainMessage);
        ex.Which.Message.Should().NotContain("net_http_message_not_success_statuscode_reason");
    }

    [Fact]
    public async Task DeactivateIntegrationAsync_On409_ThrowsWithServerDomainMessage()
    {
        const string domainMessage = "Integration 'integration-1' could not be deactivated.";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, $$"""{ "error": "{{domainMessage}}" }"""));
        var client = CreateClient(handler);

        var act = () => client.DeactivateIntegrationAsync("integration-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Which.Message.Should().Be(domainMessage);
    }

    [Fact]
    public async Task RetireIntegrationAsync_On409_ThrowsWithServerDomainMessage()
    {
        const string domainMessage = "Integration 'integration-1' could not be retired.";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, $$"""{ "error": "{{domainMessage}}" }"""));
        var client = CreateClient(handler);

        var act = () => client.RetireIntegrationAsync("integration-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Which.Message.Should().Be(domainMessage);
    }

    [Fact]
    public async Task ActivateIntegrationAsync_OnSuccess_ReturnsActivatedIntegration()
    {
        const string body = """{ "id": "integration-1", "displayName": "Sample", "moduleId": "Sample.Mod", "scopeKey": null, "state": "Active", "settings": null, "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z", "resourceContextId": "app-1", "resourceContext": null }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ActivateIntegrationAsync("integration-1");

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("/activate");
        result.State.Should().Be("Active");
    }

    private static ManagementClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string content)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(content) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private sealed class FuncHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
