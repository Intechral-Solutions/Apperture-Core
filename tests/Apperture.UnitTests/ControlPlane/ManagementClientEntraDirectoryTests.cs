using System.Net;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// A tenant directory search calls Microsoft Graph and records the discovery outcome, so the client sends it as a POST with a
/// typed JSON body. The search text travels in the body, never in the URL.
/// </summary>
public sealed class ManagementClientEntraDirectoryTests
{
    private const string Users = """{"success":true,"failureCategory":null,"summary":"ok","technicalDetail":null,"items":[{"objectId":"o1","displayName":"Dana","userPrincipalName":"dana@contoso.com","mail":null}]}""";
    private const string Groups = """{"success":true,"failureCategory":null,"summary":"ok","technicalDetail":null,"items":[]}""";

    private static (ManagementClient Client, CaptureHandler Handler) Build(string content, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CaptureHandler(status, content);
        return (new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") }), handler);
    }

    [Fact]
    public async Task A_user_search_is_a_post_and_the_search_text_travels_in_the_body()
    {
        var (client, handler) = Build(Users);

        var result = await client.SearchEntraDirectoryUsersAsync("reg 1", "dana & co", top: 10);

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/config/auth/entra-tenants/reg%201/directory/users");
        handler.LastUri.Query.Should().BeEmpty();
        handler.LastContentType.Should().Be("application/json");
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        body.GetProperty("search").GetString().Should().Be("dana & co");
        body.GetProperty("top").GetInt32().Should().Be(10);
        result!.Items.Should().ContainSingle().Which.ObjectId.Should().Be("o1");
    }

    [Fact]
    public async Task A_group_search_is_a_post_and_an_empty_search_is_still_a_json_body()
    {
        var (client, handler) = Build(Groups);

        await client.SearchEntraDirectoryGroupsAsync("reg-1");

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/config/auth/entra-tenants/reg-1/directory/groups");
        handler.LastContentType.Should().Be("application/json");
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        body.GetProperty("search").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("top").GetInt32().Should().Be(25);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Not_found_and_denied_keep_their_null_result(HttpStatusCode status)
    {
        var (client, _) = Build("{}", status);

        (await client.SearchEntraDirectoryUsersAsync("reg-1")).Should().BeNull();
        (await client.SearchEntraDirectoryGroupsAsync("reg-1")).Should().BeNull();
    }

    [Fact]
    public void The_client_interface_has_no_get_named_directory_search_left()
    {
        typeof(IManagementClient).GetMethods().Select(m => m.Name).Should()
            .NotContain(n => n.StartsWith("GetEntraDirectory", StringComparison.Ordinal));
    }

    private sealed class CaptureHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
