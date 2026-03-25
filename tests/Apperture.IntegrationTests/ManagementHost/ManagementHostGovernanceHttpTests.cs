using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.ControlPlane.Security.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostGovernanceHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostGovernanceHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        Assert.True(
            _fixture.SqlAvailable,
            $"These tests require SQL Server or LocalDB. Set {ManagementGovernanceTestConstants.ConnectionStringEnvironmentVariable} or install LocalDB. Connection attempted: {_fixture.ConnectionString}");
    }

    [Fact]
    public async Task Get_modules_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/api/modules");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_connector_definitions_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/api/connector-definitions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_connector_definitions_authenticated_without_permission_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalAuthNoPerms);
        var response = await client.GetAsync("/api/connector-definitions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_connector_definitions_with_connector_definition_read_returns_200_with_catalog_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync("/api/connector-definitions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        var first = doc.RootElement[0];
        first.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        first.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        first.GetProperty("moduleId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Get_integration_detail_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_detail_authenticated_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_integration_detail_with_integration_read_returns_200_with_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/detail");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
        root.GetProperty("moduleId").GetString().Should().Be(ManagementGovernanceTestConstants.HelloWorldModuleId);
        var readiness = root.GetProperty("readiness");
        readiness.GetProperty("integrationId").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
        readiness.TryGetProperty("isRunnable", out _).Should().BeTrue();
        readiness.GetProperty("issues").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_integration_readiness_with_integration_read_returns_200_with_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("integrationId").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
        root.TryGetProperty("isRunnable", out _).Should().BeTrue();
        root.GetProperty("issues").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_integrations_list_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync("/api/integrations");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_integration_schedules_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/schedules");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_bindings_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/bindings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_bindings_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/bindings");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_integration_bindings_with_integration_read_returns_200_array()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/bindings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_integration_schedules_with_integration_read_only_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/schedules");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_integration_schedules_with_integration_read_and_schedule_manage_returns_200()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationScheduleOperator);
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/schedules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Capabilities_match_permissions_for_connector_catalog_reader()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var dto = await client.GetFromJsonAsync<OperatorCapabilitiesDto>("/api/control/capabilities", JsonOptions);
        dto.Should().NotBeNull();
        dto!.CanViewConnectorDefinitions.Should().BeTrue();
        dto.CanViewIntegrations.Should().BeFalse();
        dto.CanManageSchedules.Should().BeFalse();
    }

    [Fact]
    public async Task Capabilities_match_permissions_for_integration_reader()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var dto = await client.GetFromJsonAsync<OperatorCapabilitiesDto>("/api/control/capabilities", JsonOptions);
        dto.Should().NotBeNull();
        dto!.CanViewIntegrations.Should().BeTrue();
        dto.CanViewConnectorDefinitions.Should().BeFalse();
        dto.CanManageSchedules.Should().BeFalse();
    }
}
