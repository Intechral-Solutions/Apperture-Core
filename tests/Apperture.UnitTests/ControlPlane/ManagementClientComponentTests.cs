using System.Net;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ManagementClientComponentTests
{
    private static (ManagementClient Client, CaptureHandler Handler) Build(HttpStatusCode status, string content)
    {
        var handler = new CaptureHandler(status, content);
        return (new ManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") }), handler);
    }

    [Fact]
    public async Task GetComponentsAsync_sends_only_supplied_filters_and_take()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"items":[],"availableKinds":[],"availableModules":[],"totalMatching":0}""");

        await client.GetComponentsAsync(kind: "connector", origin: "module", moduleId: "Fake.Module", q: "sql db", take: 50);

        var uri = handler.LastUri!.AbsoluteUri;
        uri.Should().StartWith("http://localhost/api/components?");
        uri.Should().Contain("kind=connector").And.Contain("origin=module").And.Contain("moduleId=Fake.Module")
            .And.Contain("q=sql%20db").And.Contain("take=50");
        handler.LastMethod.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetComponentsAsync_omits_unset_filters()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"items":[],"availableKinds":[],"availableModules":[],"totalMatching":0}""");

        await client.GetComponentsAsync();

        handler.LastUri!.Query.Should().Be("?take=200");
    }

    [Fact]
    public async Task GetComponentsAsync_deserializes_the_result()
    {
        var (client, _) = Build(HttpStatusCode.OK, """
            {"items":[{"kind":"connector","nativeId":"Apperture.Platform.Sql","displayName":"Platform SQL","description":null,"origin":"system","supplyingModuleId":null}],
             "availableKinds":["connector"],"availableModules":[{"moduleId":"Sample.HelloWorld"}],"totalMatching":1}
            """);

        var result = await client.GetComponentsAsync();

        result.TotalMatching.Should().Be(1);
        result.AvailableKinds.Should().Equal("connector");
        result.AvailableModules.Should().Equal(new ComponentModuleRefDto("Sample.HelloWorld"));
        result.Items.Should().ContainSingle().Which.Should().Be(new ComponentSummaryDto(
            "connector", "Apperture.Platform.Sql", "Platform SQL", null, "system", null));
    }

    [Fact]
    public async Task GetComponentsAsync_throws_with_the_status_code_when_forbidden()
    {
        var (client, _) = Build(HttpStatusCode.Forbidden, "");

        var act = async () => await client.GetComponentsAsync();

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetComponentAsync_escapes_each_segment_and_keeps_dotted_ids_intact()
    {
        var (client, handler) = Build(HttpStatusCode.NotFound, "");

        await client.GetComponentAsync("notification-definition", "platform.schedule.expected_run_missing");
        handler.LastUri!.AbsolutePath.Should().Be("/api/components/notification-definition/platform.schedule.expected_run_missing");

        await client.GetComponentAsync("connector", "Apperture.Platform.Sql");
        handler.LastUri!.AbsolutePath.Should().Be("/api/components/connector/Apperture.Platform.Sql");

        await client.GetComponentAsync("connector", "a/b c");
        handler.LastUri!.AbsoluteUri.Should().EndWith("/api/components/connector/a%2Fb%20c");
    }

    [Fact]
    public async Task GetComponentAsync_returns_null_on_404()
    {
        var (client, _) = Build(HttpStatusCode.NotFound, "");

        (await client.GetComponentAsync("connector", "missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetComponentAsync_deserializes_detail()
    {
        var (client, _) = Build(HttpStatusCode.OK, """
            {"kind":"connector","nativeId":"Apperture.Platform.Sql","displayName":"Platform SQL","description":"d","origin":"system","supplyingModuleId":null,
             "supplyingModule":null,
             "connector":{"fields":[{"key":"Host","displayName":"Host","isSecret":false,"description":null,"isRequired":true}],"supportsConnectionVerification":true},
             "notificationDefinition":null}
            """);

        var detail = await client.GetComponentAsync("connector", "Apperture.Platform.Sql");

        detail!.Connector!.SupportsConnectionVerification.Should().BeTrue();
        detail.Connector.Fields.Should().ContainSingle(f => f.Key == "Host" && f.IsRequired);
        detail.NotificationDefinition.Should().BeNull();
    }

    [Fact]
    public async Task GetComponentAsync_throws_on_server_errors()
    {
        var (client, _) = Build(HttpStatusCode.InternalServerError, "");

        var act = async () => await client.GetComponentAsync("connector", "x");

        await act.Should().ThrowAsync<HttpRequestException>();
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_content) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
