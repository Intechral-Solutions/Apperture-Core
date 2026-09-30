using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Client contract for custom role lifecycle: routes, status mapping, and the stable conflict codes.
/// </summary>
public sealed class ManagementClientRoleLifecycleTests
{
    [Fact]
    public async Task CreateRoleAsync_PostsMetadataAndGrants_ReturnsCreatedDetail()
    {
        const string body = """
            {
              "id": "role-abc",
              "name": "Operators",
              "description": "Desk",
              "isSystemRole": false,
              "permissions": [{ "action": "user.read", "resourceType": "User", "scopeKey": "instance" }],
              "assignmentCount": 0
            }
            """;
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.Created, body));
        var client = CreateClient(handler);

        var result = await client.CreateRoleAsync(new CreateRoleRequest(
            "Operators",
            "Desk",
            [new RolePermissionDto("user.read", "User", "instance")]));

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/roles");
        var sent = await handler.LastRequest.Content!.ReadAsStringAsync();
        sent.Should().Contain("\"name\":\"Operators\"");
        sent.Should().Contain("\"description\":\"Desk\"");
        sent.Should().Contain("\"action\":\"user.read\"");
        sent.Should().NotContain("isSystemRole");
        result.Id.Should().Be("role-abc");
        result.IsSystemRole.Should().BeFalse();
        result.AssignmentCount.Should().Be(0);
        result.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task CreateRoleAsync_OnDuplicateName_ThrowsConflictWithCode()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{ "error": "duplicate_name", "message": "A role with this name already exists.", "assignmentCount": null }"""));
        var client = CreateClient(handler);

        var act = () => client.CreateRoleAsync(new CreateRoleRequest("Operators"));

        var ex = await act.Should().ThrowAsync<RoleLifecycleConflictException>();
        ex.Which.Code.Should().Be(RoleLifecycleConflictException.DuplicateName);
        ex.Which.Message.Should().Be("A role with this name already exists.");
        ex.Which.AssignmentCount.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRoleMetadataAsync_PatchesNameAndDescription()
    {
        const string body = """
            {
              "id": "role-abc",
              "name": "Renamed",
              "description": null,
              "isSystemRole": false,
              "permissions": [],
              "assignmentCount": 2
            }
            """;
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.UpdateRoleMetadataAsync("role abc", new UpdateRoleMetadataRequest("Renamed", null));

        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/roles/role%20abc");
        var sent = await handler.LastRequest.Content!.ReadAsStringAsync();
        sent.Should().Contain("\"name\":\"Renamed\"");
        sent.Should().NotContain("permissions");
        result.Name.Should().Be("Renamed");
        result.AssignmentCount.Should().Be(2);
    }

    [Fact]
    public async Task UpdateRoleMetadataAsync_OnSystemRole_ThrowsConflictWithCode()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{ "error": "system_role", "message": "This built-in role cannot be changed or deleted." }"""));
        var client = CreateClient(handler);

        var act = () => client.UpdateRoleMetadataAsync("role-platform-administrator", new UpdateRoleMetadataRequest("Nope", null));

        var ex = await act.Should().ThrowAsync<RoleLifecycleConflictException>();
        ex.Which.Code.Should().Be(RoleLifecycleConflictException.SystemRole);
        ex.Which.AssignmentCount.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRoleAsync_OnSuccess_DeletesRole()
    {
        var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        await client.DeleteRoleAsync("role-abc");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/roles/role-abc");
    }

    [Fact]
    public async Task DeleteRoleAsync_WhenAssignmentsRemain_ThrowsConflictWithCount()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{ "error": "assignments_remain", "message": "This role still has assignments and cannot be deleted.", "assignmentCount": 3 }"""));
        var client = CreateClient(handler);

        var act = () => client.DeleteRoleAsync("role-abc");

        var ex = await act.Should().ThrowAsync<RoleLifecycleConflictException>();
        ex.Which.Code.Should().Be(RoleLifecycleConflictException.AssignmentsRemain);
        ex.Which.AssignmentCount.Should().Be(3);
        ex.Which.Message.Should().Be("This role still has assignments and cannot be deleted.");
    }

    [Fact]
    public async Task DeleteRoleAsync_WhenMissing_ThrowsNotFound()
    {
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.NotFound, """{ "message": "Role role-missing not found." }"""));
        var client = CreateClient(handler);

        var act = () => client.DeleteRoleAsync("role-missing");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("Role role-missing not found.");
    }

    [Fact]
    public async Task DeleteRoleAsync_WhenForbidden_ThrowsForbidden()
    {
        var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var act = () => client.DeleteRoleAsync("role-abc");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRoleAssignmentsAsync_ReturnsHoldersAndCount()
    {
        const string body = """{ "count": 1, "holders": [{ "userId": "user-1", "displayName": "Ada" }] }""";
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.GetRoleAssignmentsAsync("role-abc");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/roles/role-abc/assignments");
        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
        result.Holders.Should().ContainSingle(h => h.UserId == "user-1" && h.DisplayName == "Ada");
    }

    [Fact]
    public async Task GetRoleAssignmentsAsync_WhenMissing_ReturnsNull()
    {
        var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetRoleAssignmentsAsync("role-missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoleAsync_ReadsAssignmentCount()
    {
        const string body = """
            {
              "id": "role-abc",
              "name": "Operators",
              "description": "Desk",
              "isSystemRole": false,
              "permissions": [],
              "assignmentCount": 4
            }
            """;
        var handler = new FuncHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = CreateClient(handler);

        var result = await client.GetRoleAsync("role-abc");

        result!.AssignmentCount.Should().Be(4);
    }

    [Fact]
    public async Task SetRolePermissionsAsync_OnSystemRole_ThrowsConflictWithCode()
    {
        var handler = new FuncHandler(_ => JsonResponse(
            HttpStatusCode.Conflict,
            """{ "error": "system_role", "message": "This built-in role cannot be changed or deleted.", "assignmentCount": null }"""));
        var client = CreateClient(handler);

        var act = () => client.SetRolePermissionsAsync(
            "role-platform-administrator",
            new SetRolePermissionsRequest([new RolePermissionDto("user.read", "User", "instance")]));

        var ex = await act.Should().ThrowAsync<RoleLifecycleConflictException>();
        ex.Which.Code.Should().Be(RoleLifecycleConflictException.SystemRole);
        ex.Which.Message.Should().Be("This built-in role cannot be changed or deleted.");
    }

    private static ManagementClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string content)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
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
