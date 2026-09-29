using System.Net;
using System.Net.Http.Headers;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Schedule hard-delete uses the same error-body convention as integration delete: the operator sees the
/// API <c>error</c> text, and the status code stays on the exception so the page can tell 409 from 404.
/// </summary>
public sealed class ManagementClientScheduleDeleteTests
{
    [Fact]
    public async Task DeleteScheduleAsync_SendsDeleteToTheScheduleRoute_AndAccepts204()
    {
        HttpRequestMessage? seen = null;
        var handler = new FuncHandler(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = CreateClient(handler);

        await client.DeleteScheduleAsync("sched-1");

        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Delete);
        seen.RequestUri!.AbsolutePath.Should().Be("/api/schedules/sched-1");
    }

    [Fact]
    public async Task DeleteScheduleAsync_On409_ThrowsTheEnabledMessage()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{ "error": "Cannot delete schedule: it is still enabled. Disable it first." }"""));
        var client = CreateClient(handler);

        var act = () => client.DeleteScheduleAsync("sched-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Which.Message.Should().Be("Cannot delete schedule: it is still enabled. Disable it first.");
    }

    [Fact]
    public async Task DeleteScheduleAsync_On404_ThrowsNotFoundWithoutABody()
    {
        var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var act = () => client.DeleteScheduleAsync("sched-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.Message.Should().NotContain("sched-1");
    }

    [Fact]
    public async Task DeleteScheduleAsync_On403_ThrowsForbiddenWithoutTheScheduleId()
    {
        var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var act = () => client.DeleteScheduleAsync("sched-1");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ex.Which.Message.Should().NotContain("sched-1");
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
