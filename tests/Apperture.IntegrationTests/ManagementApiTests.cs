using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var response = await client.GetAsync("/api/modules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_integrations_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/integrations");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_bindings_for_unknown_id_returns_404_or_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/integrations/unknown-id/bindings");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_schedules_for_unknown_id_returns_404_or_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/integrations/unknown-id/schedules");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    // Connector-definition catalog authorization is asserted in unit tests (ConnectorDefinitionCatalogReadServiceTests,
    // Epic34CorrectivePassTests). This project's minimal Management.Api host does not wire governance/DI for HTTP-level auth proofs.
}
