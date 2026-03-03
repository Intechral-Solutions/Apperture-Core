using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests;

public sealed class ManagementApiTests : IClassFixture<ManagementApiWebApplicationFactory>
{
    private readonly ManagementApiWebApplicationFactory _factory;

    public ManagementApiTests(ManagementApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_modules_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/modules");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
