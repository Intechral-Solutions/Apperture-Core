using System.Net;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Each structure request logs in to an external system, so the client sends it as a POST with a typed JSON body; the operator's
/// schema/table are body values, never part of the URL.
/// </summary>
public sealed class ManagementClientSqlStructureTests
{
    private const string Ok = """{"outcome":"Succeeded","items":[],"truncated":false,"limit":500,"message":null}""";

    private static (ManagementClient Client, CaptureHandler Handler) Build(HttpStatusCode status = HttpStatusCode.OK, string content = Ok)
    {
        var handler = new CaptureHandler(status, content);
        return (new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") }), handler);
    }

    [Fact]
    public async Task Listing_schemas_is_a_POST_with_an_empty_JSON_object_body()
    {
        var (client, handler) = Build();

        await client.ListSqlSchemasAsync("conn 1");

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/connections/conn%201/tools/sql-structure/schemas");
        handler.LastContentType.Should().Be("application/json");
        handler.LastBody.Should().Be("{}");
    }

    [Fact]
    public async Task Listing_tables_is_a_POST_and_the_schema_travels_in_the_body_not_the_url()
    {
        var (client, handler) = Build();

        await client.ListSqlTablesAsync("c1", "sales & co");

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/connections/c1/tools/sql-structure/tables");
        handler.LastUri.Query.Should().BeEmpty();
        handler.LastContentType.Should().Be("application/json");
        JsonDocument.Parse(handler.LastBody!).RootElement.GetProperty("schema").GetString().Should().Be("sales & co");
    }

    [Fact]
    public async Task Listing_columns_is_a_POST_and_schema_and_table_travel_in_the_body_not_the_url()
    {
        var (client, handler) = Build();

        await client.ListSqlColumnsAsync("c1", "dbo", "Orders");

        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/connections/c1/tools/sql-structure/columns");
        handler.LastUri.Query.Should().BeEmpty();
        handler.LastContentType.Should().Be("application/json");
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        body.GetProperty("schema").GetString().Should().Be("dbo");
        body.GetProperty("table").GetString().Should().Be("Orders");
    }

    [Fact]
    public async Task The_result_is_deserialized_and_a_429_or_error_status_is_surfaced_with_its_status_code()
    {
        var (ok, _) = Build(content: """{"outcome":"Succeeded","items":[{"name":"dbo"}],"truncated":true,"limit":500,"message":"m"}""");
        var result = await ok.ListSqlSchemasAsync("c1");
        result.Items.Should().ContainSingle().Which.Name.Should().Be("dbo");
        result.Truncated.Should().BeTrue();

        var (busy, _) = Build(HttpStatusCode.TooManyRequests, "{}");
        var act = async () => await busy.ListSqlSchemasAsync("c1");
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_local_availability_lookup_stays_a_GET()
    {
        var (client, handler) = Build(content: """{"sqlStructureBrowser":true}""");

        await client.GetConnectionToolsAsync("c1");

        handler.LastMethod.Should().Be(HttpMethod.Get);
        handler.LastUri!.AbsoluteUri.Should().Be("http://localhost/api/connections/c1/tools");
    }

    [Fact]
    public void The_client_interface_has_no_Get_named_structure_method_left()
    {
        typeof(IManagementClient).GetMethods().Select(m => m.Name).Should()
            .NotContain(n => n.StartsWith("GetSql", StringComparison.Ordinal));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public CaptureHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

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
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
