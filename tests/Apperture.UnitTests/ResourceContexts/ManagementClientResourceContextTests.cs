using System.Net;
using System.Net.Http.Headers;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 3 (Task 1): client contract for ResourceContextsController. Written against the REAL,
/// merged controller shape (top-level DTOs with IsReserved, two distinct 409 body shapes on Archive,
/// omit-not-zero Summary semantics) -- not the Plan 3 brief's outdated pre-Plan-2 sketch.
/// </summary>
public sealed class ManagementClientResourceContextTests
{
    [Fact]
    public async Task GetResourceContextsAsync_DefaultsToExcludingArchived()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        await client.GetResourceContextsAsync();

        handler.LastRequest!.RequestUri!.ToString().Should().NotContain("includeArchived=True", "the default call must not request archived contexts");
    }

    [Fact]
    public async Task GetResourceContextsAsync_WithIncludeArchivedTrue_AppendsQueryParam()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        await client.GetResourceContextsAsync(includeArchived: true);

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("includeArchived=true");
    }

    [Fact]
    public async Task GetResourceContextsAsync_DeserializesIsReserved()
    {
        const string body = """
            [
              { "id": "shared", "name": "Shared", "slug": "shared", "kind": "Shared", "state": "Active", "isReserved": true, "description": null, "createdAtUtc": "2026-01-01T00:00:00Z", "updatedAtUtc": "2026-01-01T00:00:00Z" },
              { "id": "app-1", "name": "App 1", "slug": "app-1", "kind": "Application", "state": "Active", "isReserved": false, "description": null, "createdAtUtc": "2026-01-01T00:00:00Z", "updatedAtUtc": "2026-01-01T00:00:00Z" }
            ]
            """;
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.GetResourceContextsAsync();

        result.Should().HaveCount(2);
        result[0].IsReserved.Should().BeTrue();
        result[1].IsReserved.Should().BeFalse();
    }

    [Fact]
    public async Task GetResourceContextAsync_On404_ReturnsNull()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.NotFound, "{}"));
        var client = CreateClient(handler);

        var result = await client.GetResourceContextAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateResourceContextAsync_PostsAndReturnsCreated()
    {
        const string body = """{ "id": "app-2", "name": "App 2", "slug": "app-2", "kind": "Application", "state": "Active", "isReserved": false, "description": null, "createdAtUtc": "2026-01-01T00:00:00Z", "updatedAtUtc": "2026-01-01T00:00:00Z" }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Created, body));
        var client = CreateClient(handler);

        var result = await client.CreateResourceContextAsync(new CreateResourceContextRequest("App 2", null, null));

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        result.Id.Should().Be("app-2");
    }

    [Fact]
    public async Task UpdateResourceContextAsync_On409Reserved_ThrowsHttpRequestExceptionWithConflictStatus()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, """{ "error": "The reserved ResourceContext 'Shared' cannot be modified." }"""));
        var client = CreateClient(handler);

        var act = () => client.UpdateResourceContextAsync("shared", new UpdateResourceContextRequest("New Name", null, null));

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ArchiveResourceContextAsync_OnSuccess_ReturnsArchivedContext()
    {
        const string body = """{ "id": "app-1", "name": "App 1", "slug": "app-1", "kind": "Application", "state": "Archived", "isReserved": false, "description": null, "createdAtUtc": "2026-01-01T00:00:00Z", "updatedAtUtc": "2026-01-01T00:00:00Z" }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.ArchiveResourceContextAsync("app-1");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("/archive");
        result.State.Should().Be("Archived");
    }

    [Fact]
    public async Task ArchiveResourceContextAsync_On409ReservedShape_ThrowsWithReservedReasonAndNoSummary()
    {
        // The reserved-context 409 body is a plain { error } object -- NOT the ownership summary shape.
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, """{ "error": "The reserved ResourceContext 'Shared' cannot be archived." }"""));
        var client = CreateClient(handler);

        var act = () => client.ArchiveResourceContextAsync("shared");

        var ex = await act.Should().ThrowAsync<ResourceContextArchiveConflictException>();
        ex.Which.Reason.Should().Be(ResourceContextArchiveConflictReason.Reserved);
        ex.Which.OwnershipSummary.Should().BeNull();
        ex.Which.Message.Should().Contain("Shared");
    }

    [Fact]
    public async Task ArchiveResourceContextAsync_On409OwnershipShape_ThrowsWithOwnershipSummary()
    {
        // The ownership-blocked 409 body is the full ResourceContextOwnershipSummaryDto -- NOT a { error } object.
        const string body = """{ "liveIntegrations": 2, "retiredIntegrations": 0, "activeConnections": 1, "retiredConnections": 0, "enabledSchedules": 0, "disabledSchedules": 0 }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Conflict, body));
        var client = CreateClient(handler);

        var act = () => client.ArchiveResourceContextAsync("canvas-id");

        var ex = await act.Should().ThrowAsync<ResourceContextArchiveConflictException>();
        ex.Which.Reason.Should().Be(ResourceContextArchiveConflictReason.OwnershipBlocked);
        ex.Which.OwnershipSummary.Should().NotBeNull();
        ex.Which.OwnershipSummary!.LiveIntegrations.Should().Be(2);
        ex.Which.OwnershipSummary!.ActiveConnections.Should().Be(1);
    }

    [Fact]
    public async Task UnarchiveResourceContextAsync_PostsToUnarchiveAndReturnsContext()
    {
        const string body = """{ "id": "app-1", "name": "App 1", "slug": "app-1", "kind": "Application", "state": "Active", "isReserved": false, "description": null, "createdAtUtc": "2026-01-01T00:00:00Z", "updatedAtUtc": "2026-01-01T00:00:00Z" }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.UnarchiveResourceContextAsync("app-1");

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("/unarchive");
        result.State.Should().Be("Active");
    }

    [Fact]
    public async Task GetResourceContextSummaryAsync_OmitsContextsWithNoResources()
    {
        // Per the real controller's XML doc: a context owning nothing is OMITTED, not zero-filled.
        const string body = """{ "app-1": { "liveIntegrations": 3, "retiredIntegrations": null, "activeConnections": null, "retiredConnections": null, "enabledSchedules": null, "disabledSchedules": null } }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.GetResourceContextSummaryAsync();

        result.Should().ContainKey("app-1");
        result.Should().NotContainKey("app-2");
        result["app-1"].LiveIntegrations.Should().Be(3);
    }

    [Fact]
    public async Task CreateResourceContextAsync_On403_ThrowsHttpRequestExceptionWithForbiddenStatus()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Forbidden, "{}"));
        var client = CreateClient(handler);

        var act = () => client.CreateResourceContextAsync(new CreateResourceContextRequest("X", null, null));

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
