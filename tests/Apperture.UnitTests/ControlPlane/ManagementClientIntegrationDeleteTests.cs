using System.Net;
using System.Net.Http.Headers;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// DeleteIntegrationAsync was the one delete-style ManagementClient method that called the raw
/// HttpResponseMessage.EnsureSuccessStatusCode() instead of the house EnsureSuccessOrThrowAsync helper
/// every other method uses -- discarding the backend's { error } body (e.g. "Cannot delete integration:
/// 2 binding(s) reference it.") in favor of a generic message that Blazor WebAssembly's trimmed build
/// renders unreadably (the raw resource key instead of formatted text).
/// </summary>
public sealed class ManagementClientIntegrationDeleteTests
{
    [Fact]
    public async Task DeleteIntegrationAsync_On400WithErrorBody_ThrowsWithTheBackendMessage()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.BadRequest,
            """{ "error": "Cannot delete integration: 2 binding(s) reference it." }"""));
        var client = CreateClient(handler);

        var act = () => client.DeleteIntegrationAsync("integration-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Be("Cannot delete integration: 2 binding(s) reference it.",
            "the operator must see why the delete was refused, not a generic status-code message");
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

        public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
