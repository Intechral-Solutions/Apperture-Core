using System.Net;
using System.Text;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>The typed client for <c>PUT /api/users/{id}/username</c>: request shape, success body, and errors that keep the server's code.</summary>
public sealed class ManagementClientRenameUserTests
{
    private sealed class Capture : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public Capture(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static (ManagementClient Client, Capture Http) Create(HttpStatusCode status, string body)
    {
        var http = new Capture(status, body);
        return (new ManagementClient(new HttpClient(http) { BaseAddress = new Uri("https://api.example/api/") }), http);
    }

    [Fact]
    public async Task Rename_puts_the_username_to_the_user_id_route_and_returns_the_canonical_result()
    {
        var (client, http) = Create(HttpStatusCode.OK, """{"userId":"user-7f3a","username":"alice.renamed","outcome":"assigned"}""");

        var result = await client.RenameUserAsync("user-7f3a", "Alice.Renamed");

        http.Method.Should().Be(HttpMethod.Put);
        http.Path.Should().Be("/api/users/user-7f3a/username");
        http.RequestBody.Should().Contain("\"username\":\"Alice.Renamed\"");
        result.UserId.Should().Be("user-7f3a");
        result.Username.Should().Be("alice.renamed");
        result.Outcome.Should().Be("assigned");
    }

    [Fact]
    public async Task Rename_escapes_the_user_id_in_the_route()
    {
        var (client, http) = Create(HttpStatusCode.OK, """{"userId":"a/b","username":"alice.renamed","outcome":"assigned"}""");

        await client.RenameUserAsync("a/b c", "alice.renamed");

        http.Path.Should().Be("/api/users/a%2Fb%20c/username");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "username_invalid")]
    [InlineData(HttpStatusCode.BadRequest, "username_reserved")]
    [InlineData(HttpStatusCode.Conflict, "username_taken")]
    [InlineData(HttpStatusCode.Conflict, "username_concurrent_change")]
    [InlineData(HttpStatusCode.Conflict, "UnsafeOperation")]
    public async Task Rename_failures_keep_the_status_the_server_message_and_the_error_code(HttpStatusCode status, string code)
    {
        var (client, _) = Create(status, $$"""{"message":"Server says no.","code":"{{code}}"}""");

        var act = () => client.RenameUserAsync("user-7f3a", "bob");

        var ex = (await act.Should().ThrowAsync<UsernameRenameException>()).Which;
        ex.StatusCode.Should().Be(status);
        ex.Code.Should().Be(code);
        ex.Message.Should().Be("Server says no.");
    }

    [Fact]
    public async Task Rename_forbidden_and_not_found_have_plain_messages_without_a_body()
    {
        var (forbiddenClient, _) = Create(HttpStatusCode.Forbidden, "");
        var forbidden = (await ((Func<Task>)(() => forbiddenClient.RenameUserAsync("u", "alice"))).Should().ThrowAsync<UsernameRenameException>()).Which;
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Message.Should().Contain("Not authorized");

        var (missingClient, _) = Create(HttpStatusCode.NotFound, "");
        var missing = (await ((Func<Task>)(() => missingClient.RenameUserAsync("u", "alice"))).Should().ThrowAsync<UsernameRenameException>()).Which;
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Code.Should().BeNull();
    }
}
