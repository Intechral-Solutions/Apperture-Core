using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Sample.HelloWorld.Contracts;
using Unisa.CampusNexus.PaymentPlans;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostIntegrationConfigurationHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostIntegrationConfigurationHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        Assert.True(
            _fixture.SqlAvailable,
            $"These tests require a reachable test SQL Server. {SqlServerTestConfiguration.DescribeActiveConfiguration()}");
    }

    [Fact]
    public async Task Get_configuration_declarations_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/configuration/declarations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_configuration_declarations_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/configuration/declarations");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_configuration_declarations_hello_world_returns_optional_settings_and_empty_connections()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/configuration/declarations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<IntegrationConfigurationDeclarationsDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.ModuleId.Should().Be(ManagementGovernanceTestConstants.HelloWorldModuleId);
        dto.Connections.Should().BeEmpty();
        dto.Settings.Should().NotBeEmpty();
        dto.Settings.Should().OnlyContain(s => !s.IsRequired);
        dto.Settings.Should().Contain(s => s.Key == HelloWorldDemoSettingKeys.GreetingPrefix);
        dto.Procedures.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_configuration_declarations_unisa_lists_connection_requirements_with_operator_labels()
    {
        AssertSqlAvailable();
        using var creator = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var create = await creator.PostAsJsonAsync("/api/integrations", new
        {
            displayName = $"SDK-6D declarations {Guid.NewGuid():N}"[..28],
            moduleId = "Unisa.CampusNexus.PaymentPlans",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var integrationId = created.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();

        using var reader = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await reader.GetAsync($"/api/integrations/{integrationId}/configuration/declarations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<IntegrationConfigurationDeclarationsDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.ModuleId.Should().Be("Unisa.CampusNexus.PaymentPlans");
        dto.Connections.Should().HaveCount(3);
        dto.Connections.Should().Contain(c =>
            c.Key == UnisaCampusNexusPaymentPlansConstants.SlotCnsApi
            && c.CapabilityKind == "http-api"
            && c.CapabilityLabel == "HTTP API"
            && !string.IsNullOrWhiteSpace(c.TechnicalContractType));
        dto.Connections.Should().OnlyContain(c => c.CapabilityLabel != c.TechnicalContractType);
        dto.Procedures.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_configuration_declarations_foreign_module_without_contributor_returns_404()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationForeignModuleId}/configuration/declarations");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("no SDK configuration declarations");
    }

    [Fact]
    public async Task Get_procedure_readiness_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.greet/readiness");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_procedure_readiness_unisa_returns_issues_array_when_not_ready()
    {
        AssertSqlAvailable();
        using var creator = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var create = await creator.PostAsJsonAsync("/api/integrations", new
        {
            displayName = $"SDK-6D proc readiness {Guid.NewGuid():N}"[..30],
            moduleId = "Unisa.CampusNexus.PaymentPlans",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var integrationId = created.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();

        using var reader = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await reader.GetAsync(
            $"/api/integrations/{integrationId}/procedures/{UnisaCampusNexusPaymentPlansConstants.ProcedureProcessDailyFiles}/readiness");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<IntegrationProcedureReadinessDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.IsRunnable.Should().BeFalse();
        dto.BlockingIssueCount.Should().BeGreaterThan(0);
        dto.Issues.Should().NotBeEmpty();
        dto.Issues.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Code));
    }

    [Fact]
    public async Task Get_integration_settings_list_returns_module_settings_without_plaintext_secrets()
    {
        AssertSqlAvailable();

        using var authoring = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var create = await authoring.PostAsJsonAsync("/api/integrations", new
        {
            displayName = $"SDK-6D settings list {Guid.NewGuid():N}"[..28],
            moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var integrationId = created.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();

        using var reader = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var listResponse = await reader.GetAsync($"/api/integrations/{integrationId}/settings");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<PlatformSettingResolvedDto>>(JsonOptions);
        rows.Should().NotBeNull();
        rows!.Should().NotBeEmpty();
        rows.Should().Contain(r => r.Key == HelloWorldDemoSettingKeys.GreetingPrefix);
        rows.Where(r => r.IsSensitive).Should().OnlyContain(r => string.IsNullOrEmpty(r.CurrentValue));
    }
}
