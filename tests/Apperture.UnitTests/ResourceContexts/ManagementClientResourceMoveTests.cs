using System.Net;
using System.Net.Http.Headers;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B Plan 5 (Task 3): client contract for the six ownership move-preview/move methods. CTX-3A shipped
/// exactly this class of regression once already (Activate/Deactivate/RetireIntegrationAsync calling bare
/// EnsureSuccessStatusCode(), which rendered a 409 to the operator as the untranslated resource key
/// "net_http_message_not_success_statuscode_reason, 409, Conflict" instead of the server's real message).
/// These tests exist to make sure that never happens again for the move surface.
/// </summary>
public sealed class ManagementClientResourceMoveTests
{
    private const string PreviewBody = """
        {
          "resourceType": "Integration",
          "resourceId": "int-1",
          "source": { "id": "app-1", "name": "App 1", "slug": "app-1" },
          "target": { "id": "app-2", "name": "App 2", "slug": "app-2" },
          "canMove": true,
          "blockers": [],
          "effects": []
        }
        """;

    private const string BlockedPreviewBody = """
        {
          "resourceType": "Integration",
          "resourceId": "int-1",
          "source": { "id": "app-1", "name": "App 1", "slug": "app-1" },
          "target": { "id": "app-2", "name": "App 2", "slug": "app-2" },
          "canMove": false,
          "blockers": [
            { "code": "binding_connection_unreachable", "message": "A bound connection is not reachable from the target context.", "relatedResourceType": "Connection", "relatedResourceId": "conn-1", "relatedDisplayName": "Conn 1", "relatedResourceContextId": "app-1" }
          ],
          "effects": []
        }
        """;

    [Fact]
    public async Task GetIntegrationOwnershipMovePreviewAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.GetIntegrationOwnershipMovePreviewAsync("int-1", "app-2");

        result.CanMove.Should().BeTrue();
        result.ResourceType.Should().Be("Integration");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("integrations/int-1/ownership/move-preview");
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("targetResourceContextId=app-2");
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveIntegrationOwnershipAsync("int-1", "app-2");

        result.CanMove.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("integrations/int-1/ownership/move");
    }

    [Fact]
    public async Task GetConnectionOwnershipMovePreviewAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.GetConnectionOwnershipMovePreviewAsync("conn-1", "app-2");

        result.CanMove.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("connections/conn-1/ownership/move-preview");
    }

    [Fact]
    public async Task MoveConnectionOwnershipAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveConnectionOwnershipAsync("conn-1", "app-2");

        result.CanMove.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("connections/conn-1/ownership/move");
    }

    [Fact]
    public async Task GetScheduleOwnershipMovePreviewAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.GetScheduleOwnershipMovePreviewAsync("sched-1", "app-2");

        result.CanMove.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("schedules/sched-1/ownership/move-preview");
    }

    [Fact]
    public async Task MoveScheduleOwnershipAsync_On200_ReturnsCanMoveTrue()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, PreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveScheduleOwnershipAsync("sched-1", "app-2");

        result.CanMove.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("schedules/sched-1/ownership/move");
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On409WithPreviewBody_ReturnsBlockedPreview_DoesNotThrow()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, BlockedPreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveIntegrationOwnershipAsync("int-1", "app-2");

        result.CanMove.Should().BeFalse();
        result.Blockers.Should().ContainSingle();
        result.Blockers[0].Code.Should().Be(ResourceMoveBlockerCodes.BindingConnectionUnreachable);
    }

    [Fact]
    public async Task MoveConnectionOwnershipAsync_On409WithPreviewBody_ReturnsBlockedPreview_DoesNotThrow()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, BlockedPreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveConnectionOwnershipAsync("conn-1", "app-2");

        result.CanMove.Should().BeFalse();
    }

    [Fact]
    public async Task MoveScheduleOwnershipAsync_On409WithPreviewBody_ReturnsBlockedPreview_DoesNotThrow()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, BlockedPreviewBody));
        var client = CreateClient(handler);

        var result = await client.MoveScheduleOwnershipAsync("sched-1", "app-2");

        result.CanMove.Should().BeFalse();
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On409BusyShape_ThrowsInvalidOperationExceptionWithServerMessage()
    {
        const string body = """{ "error": "Another ownership change is in progress. Wait a moment and try again.", "code": "ownership_operation_busy" }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, body));
        var client = CreateClient(handler);

        var act = () => client.MoveIntegrationOwnershipAsync("int-1", "app-2");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("Another ownership change is in progress. Wait a moment and try again.");
        ex.Which.Message.Should().NotContain("ownership_operation_busy");
        ex.Which.Message.Should().NotContain("net_http_message_not_success_statuscode_reason");
    }

    [Fact]
    public async Task MoveConnectionOwnershipAsync_On409BusyShape_ThrowsInvalidOperationExceptionWithServerMessage()
    {
        const string body = """{ "error": "Another ownership change is in progress. Wait a moment and try again.", "code": "ownership_operation_busy" }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, body));
        var client = CreateClient(handler);

        var act = () => client.MoveConnectionOwnershipAsync("conn-1", "app-2");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("Another ownership change is in progress. Wait a moment and try again.");
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On400_ThrowsInvalidOperationExceptionWithServerMessage()
    {
        const string body = """{ "error": "The target ResourceContext was not found." }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.BadRequest, body));
        var client = CreateClient(handler);

        var act = () => client.MoveIntegrationOwnershipAsync("int-1", "missing-target");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("The target ResourceContext was not found.");
    }

    [Fact]
    public async Task MoveScheduleOwnershipAsync_On400_ThrowsInvalidOperationExceptionWithServerMessage()
    {
        const string body = """{ "error": "The Schedule is already owned by the target ResourceContext." }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.BadRequest, body));
        var client = CreateClient(handler);

        var act = () => client.MoveScheduleOwnershipAsync("sched-1", "app-1");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("The Schedule is already owned by the target ResourceContext.");
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On403_ThrowsHttpRequestExceptionWithForbiddenStatus()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Forbidden, "{}"));
        var client = CreateClient(handler);

        var act = () => client.MoveIntegrationOwnershipAsync("int-1", "app-2");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetIntegrationOwnershipMovePreviewAsync_On404_ThrowsHttpRequestExceptionWithNotFoundStatusAndServerMessage()
    {
        const string body = """{ "error": "Integration 'missing' was not found." }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.NotFound, body));
        var client = CreateClient(handler);

        var act = () => client.GetIntegrationOwnershipMovePreviewAsync("missing", "app-2");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.Message.Should().Be("Integration 'missing' was not found.");
    }

    [Fact]
    public async Task MoveIntegrationOwnershipAsync_On500_ThrowsHttpRequestExceptionWithInternalServerErrorStatus()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        var client = CreateClient(handler);

        var act = () => client.MoveIntegrationOwnershipAsync("int-1", "app-2");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
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
