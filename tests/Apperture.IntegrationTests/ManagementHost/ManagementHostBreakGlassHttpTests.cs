using System.Net;
using System.Net.Http.Json;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostBreakGlassHttpTests
{
    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostBreakGlassHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Post_break_glass_when_disabled_returns_not_found_and_does_not_create_session()
    {
        Assert.True(_fixture.SqlAvailable, SqlServerTestConfiguration.DescribeActiveConfiguration());

        var client = _fixture.Factory!.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/break-glass",
            new LocalLoginRequest("any-user", "any-password"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<BreakGlassAuthErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be(BreakGlassAuthErrorCodes.Disabled);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();

        var me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
