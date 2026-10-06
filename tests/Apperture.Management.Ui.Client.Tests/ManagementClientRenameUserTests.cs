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
        private readonly string _contentType;

        public Capture(HttpStatusCode status, string body, string contentType = "application/json")
        {
            _status = status;
            _body = body;
            _contentType = contentType;
        }

        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, _contentType) };
        }
    }

    private static (ManagementClient Client, Capture Http) Create(HttpStatusCode status, string body, string contentType = "application/json")
    {
        var http = new Capture(status, body, contentType);
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
        ex.OutcomeUnknown.Should().BeFalse("a 4xx is the API refusing the request, so nothing changed");
    }

    [Theory]
    [InlineData("<html><body>Bad request</body></html>", "text/html")]
    [InlineData("not json at all", "application/json")]
    [InlineData("", "application/json")]
    public async Task A_refusal_without_a_readable_body_is_a_refusal_with_a_plain_message_not_a_parse_failure(string body, string contentType)
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, body, contentType);

        var act = () => client.RenameUserAsync("user-7f3a", "bob");

        var ex = (await act.Should().ThrowAsync<UsernameRenameException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Be("Could not change the username.");
        ex.Code.Should().BeNull();
        ex.OutcomeUnknown.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"message":"Server says no.","code":"username_taken"}""", "application/json")]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>Bad gateway</body></html>", "text/html")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", "application/json")]
    [InlineData(HttpStatusCode.GatewayTimeout, "<html>Gateway time-out</html>", "text/html")]
    [InlineData(HttpStatusCode.RequestTimeout, "", "application/json")]
    public async Task A_server_or_gateway_error_is_an_unknown_outcome_never_a_claim_that_the_rename_failed(HttpStatusCode status, string body, string contentType)
    {
        // The API commits the rename before it writes the audit row, and a gateway can fail after the API answered, so these
        // statuses do not prove the rename was refused. Whatever the body says, the operator is told the result is unknown.
        var (client, _) = Create(status, body, contentType);

        var act = () => client.RenameUserAsync("user-7f3a", "bob");

        var ex = (await act.Should().ThrowAsync<UsernameRenameException>()).Which;
        ex.StatusCode.Should().Be(status);
        ex.OutcomeUnknown.Should().BeTrue();
        ex.Message.Should().StartWith("The server could not complete the request.")
            .And.Contain(UsernameRenameException.ConfirmByRefreshing);
        ex.Message.Should().NotContainAny("Could not change", "failed", "was not changed", "Server says no.");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    public void Only_a_server_error_or_a_request_timeout_leaves_the_outcome_unknown(HttpStatusCode status, bool unknown) =>
        UsernameRenameException.IsOutcomeUnknown(status).Should().Be(unknown);

    [Theory]
    [InlineData("not json at all", typeof(System.Text.Json.JsonException))]
    [InlineData("null", typeof(InvalidOperationException))]
    public async Task A_success_whose_body_cannot_be_read_throws_so_the_caller_can_say_the_change_was_not_confirmed(string body, Type expected)
    {
        var (client, _) = Create(HttpStatusCode.OK, body);

        var act = () => client.RenameUserAsync("user-7f3a", "bob");

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(expected);
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
