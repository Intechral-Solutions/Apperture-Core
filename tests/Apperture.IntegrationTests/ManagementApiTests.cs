using System.Net;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Smoke tests for the minimal Management.Api library host (no governance). For real HTTP auth proofs, see
/// <see cref="ManagementHost.ManagementHostGovernanceHttpTests"/>.
/// </summary>
public sealed class ManagementApiTests
{
    [Fact]
    public async Task Management_Api_stub_host_root_returns_200()
    {
        await using var factory = new ManagementApiStubWebApplicationFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
