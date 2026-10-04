using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            $"These tests require SQL Server or LocalDB. {SqlServerTestConfiguration.DescribeActiveConfiguration()} " +
            $"Connection attempted: {SqlServerTestConfiguration.DescribeConnectionSummary(_fixture.ConnectionString)}");
    }

    private static (string Host, string Port, string Username, string Password) GetSftpTestSettings() =>
        (
            Environment.GetEnvironmentVariable(ManagementGovernanceTestConstants.SftpHostEnvironmentVariable) ?? ManagementGovernanceTestConstants.DefaultSftpHost,
            Environment.GetEnvironmentVariable(ManagementGovernanceTestConstants.SftpPortEnvironmentVariable) ?? ManagementGovernanceTestConstants.DefaultSftpPort,
            Environment.GetEnvironmentVariable(ManagementGovernanceTestConstants.SftpUsernameEnvironmentVariable) ?? ManagementGovernanceTestConstants.DefaultSftpUsername,
            Environment.GetEnvironmentVariable(ManagementGovernanceTestConstants.SftpPasswordEnvironmentVariable) ?? ManagementGovernanceTestConstants.DefaultSftpPassword
        );

    private static string? FindIssueForConnection(JsonElement readiness, string code, string connectionId)
    {
        foreach (var issue in readiness.GetProperty("issues").EnumerateArray())
        {
            if (string.Equals(issue.GetProperty("code").GetString(), code, StringComparison.Ordinal)
                && string.Equals(issue.TryGetProperty("connectionId", out var connEl) ? connEl.GetString() : null, connectionId, StringComparison.Ordinal))
            {
                return issue.TryGetProperty("severity", out var sev) ? sev.GetString() : null;
            }
        }

        return null;
    }

    private static async Task<bool> IsTcpEndpointReachableAsync(string host, string port, int timeoutMs = 3000)
    {
        if (!int.TryParse(port, out var parsedPort))
            return false;

        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(host, parsedPort, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
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
        first.GetProperty("capabilities").GetString().Should().NotBeNullOrEmpty();
        first.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Post_connection_test_connect_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.PostAsync("/api/connections/doesnotexist/test-connect", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_connection_test_connect_without_permission_returns_403()
    {
        AssertSqlAvailable();
        using var creator = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var create = await creator.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sql",
            name = $"Epic59 verify perm {Guid.NewGuid():N}"[..24],
            config = new Dictionary<string, string> { ["Server"] = "localhost", ["Database"] = "db", ["UserId"] = "sa" },
            secretValues = new Dictionary<string, string> { ["Password"] = "TestPassword123!" },
            scopeKey = "instance"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var connectionId = created.RootElement.GetProperty("id").GetString();
        connectionId.Should().NotBeNullOrWhiteSpace();

        using var noPerms = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalAuthNoPerms);
        var response = await noPerms.PostAsync($"/api/connections/{connectionId}/test-connect", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_connection_test_connect_sql_runs_probe_against_configured_database()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        var csb = new SqlConnectionStringBuilder(_fixture.ConnectionString);
        var config = new Dictionary<string, string>
        {
            ["Server"] = csb.DataSource,
            ["Database"] = csb.InitialCatalog,
        };
        IReadOnlyDictionary<string, string>? secretValues = null;
        if (!csb.IntegratedSecurity)
        {
            config["UserId"] = csb.UserID;
            secretValues = new Dictionary<string, string> { ["Password"] = csb.Password };
        }

        var create = await client.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sql",
            name = $"Epic59 verify sql {Guid.NewGuid():N}"[..24],
            config,
            secretRefs = new Dictionary<string, string>(),
            secretValues,
            scopeKey = "instance"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var connectionId = created.RootElement.GetProperty("id").GetString();
        connectionId.Should().NotBeNullOrWhiteSpace();

        var verify = await client.PostAsync($"/api/connections/{connectionId}/test-connect", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verifyDoc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        verifyDoc.RootElement.GetProperty("supportsVerification").GetBoolean().Should().BeTrue();
        verifyDoc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Success");
        verifyDoc.RootElement.GetProperty("lastVerificationSummary").GetString().Should().Contain("SELECT 1");
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
    public async Task Post_integration_create_then_detail_shows_required_slots_and_actionable_readiness()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await client.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic57 slot workflow",
                moduleId = "Unisa.CampusNexus.PaymentPlans",
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var createdDoc = JsonDocument.Parse(createdJson);
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var createdIntegrationId = integrationId!;

        var detailResponse = await client.GetAsync($"/api/integrations/{createdIntegrationId}/detail");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        using var detailDoc = JsonDocument.Parse(detailJson);
        var root = detailDoc.RootElement;
        root.GetProperty("moduleId").GetString().Should().Be("Unisa.CampusNexus.PaymentPlans");
        var requiredSlots = root.GetProperty("requiredSlots");
        requiredSlots.ValueKind.Should().Be(JsonValueKind.Array);
        requiredSlots.GetArrayLength().Should().BeGreaterThan(0);

        var readiness = root.GetProperty("readiness");
        readiness.GetProperty("isRunnable").GetBoolean().Should().BeFalse();
        var issues = readiness.GetProperty("issues");
        issues.ValueKind.Should().Be(JsonValueKind.Array);
        issues.EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("missing_slot_binding");
    }

    [Fact]
    public async Task Post_integration_create_connection_then_bind_improves_missing_slot_binding_readiness()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        var createIntegrationResponse = await client.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic58 create-and-bind",
                moduleId = "Unisa.CampusNexus.PaymentPlans",
                scopeKey = "instance"
            });
        createIntegrationResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createIntegrationResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var createdIntegrationId = integrationId!;

        var beforeDetailResponse = await client.GetAsync($"/api/integrations/{createdIntegrationId}/detail");
        beforeDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var beforeDoc = JsonDocument.Parse(await beforeDetailResponse.Content.ReadAsStringAsync());
        var requiredSlots = beforeDoc.RootElement.GetProperty("requiredSlots").EnumerateArray().ToArray();
        requiredSlots.Length.Should().BeGreaterThan(0);
        var targetSlot = requiredSlots.FirstOrDefault(slot =>
            slot.GetProperty("contractType").GetString()?.Contains("IDbConnection", StringComparison.Ordinal) == true);
        targetSlot.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var slotName = targetSlot.GetProperty("slotName").GetString();
        var contractType = targetSlot.GetProperty("contractType").GetString();
        slotName.Should().NotBeNullOrWhiteSpace();
        contractType.Should().NotBeNullOrWhiteSpace();

        var beforeMissingSlotIssueCount = beforeDoc.RootElement.GetProperty("readiness").GetProperty("issues")
            .EnumerateArray()
            .Count(issue => string.Equals(issue.GetProperty("code").GetString(), "missing_slot_binding", StringComparison.Ordinal));

        var createConnectionResponse = await client.PostAsJsonAsync(
            "/api/connections",
            new
            {
                connectorDefinitionId = "Apperture.Platform.Sql",
                name = $"Epic58 SQL {Guid.NewGuid():N}"[..24],
                config = new Dictionary<string, string>
                {
                    ["Server"] = "localhost",
                    ["Database"] = "TestDb",
                    ["UserId"] = "sa"
                },
                secretValues = new Dictionary<string, string>
                {
                    ["Password"] = "TestPassword123!"
                },
                scopeKey = "instance"
            });
        createConnectionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var connectionDoc = JsonDocument.Parse(await createConnectionResponse.Content.ReadAsStringAsync());
        var connectionId = connectionDoc.RootElement.GetProperty("id").GetString();
        connectionId.Should().NotBeNullOrWhiteSpace();
        var createdConnectionId = connectionId!;

        var eligibleResponse = await client.GetAsync(
            $"/api/integrations/{createdIntegrationId}/bindings/eligible-connections?slotName={Uri.EscapeDataString(slotName!)}&contractType={Uri.EscapeDataString(contractType!)}");
        eligibleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var eligibleDoc = JsonDocument.Parse(await eligibleResponse.Content.ReadAsStringAsync());
        eligibleDoc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().Contain(createdConnectionId);

        var bindResponse = await client.PostAsJsonAsync(
            $"/api/integrations/{createdIntegrationId}/bindings",
            new
            {
                slotName,
                contractType,
                connectionId = createdConnectionId
            });
        bindResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterDetailResponse = await client.GetAsync($"/api/integrations/{createdIntegrationId}/detail");
        afterDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var afterDoc = JsonDocument.Parse(await afterDetailResponse.Content.ReadAsStringAsync());
        afterDoc.RootElement.GetProperty("bindings")
            .EnumerateArray()
            .Any(b =>
                string.Equals(b.GetProperty("slotName").GetString(), slotName, StringComparison.Ordinal)
                && string.Equals(b.GetProperty("connectionId").GetString(), createdConnectionId, StringComparison.Ordinal))
            .Should().BeTrue();

        var afterMissingSlotIssueCount = afterDoc.RootElement.GetProperty("readiness").GetProperty("issues")
            .EnumerateArray()
            .Count(issue => string.Equals(issue.GetProperty("code").GetString(), "missing_slot_binding", StringComparison.Ordinal));
        afterMissingSlotIssueCount.Should().BeLessThan(beforeMissingSlotIssueCount);
        afterDoc.RootElement.GetProperty("readiness").GetProperty("issues")
            .EnumerateArray()
            .Any(issue => string.Equals(issue.GetProperty("code").GetString(), "connection_unverified", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Post_integration_activate_deactivate_updates_operational_state()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await client.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic60 activation test",
                moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();

        var deactivate = await client.PostAsync($"/api/integrations/{integrationId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var deactivatedDoc = JsonDocument.Parse(await deactivate.Content.ReadAsStringAsync()))
        {
            deactivatedDoc.RootElement.GetProperty("state").GetString().Should().Be("Inactive");
        }

        var activate = await client.PostAsync($"/api/integrations/{integrationId}/activate", null);
        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        using var activatedDoc = JsonDocument.Parse(await activate.Content.ReadAsStringAsync());
        activatedDoc.RootElement.GetProperty("state").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Procedure_launch_returns_integration_inactive_when_inactive()
    {
        AssertSqlAvailable();
        using var authoring = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await authoring.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic60 inactive launch gate",
                moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var deactivate = await authoring.PostAsync($"/api/integrations/{integrationId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var launcher = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await launcher.PostAsJsonAsync(
            $"/api/integrations/{integrationId}/procedures/Sample.HelloWorld.procedures.greet/launch",
            new { parameters = new Dictionary<string, object?> { ["name"] = "epic60" } });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("integration_inactive");
    }

    [Fact]
    public async Task Workflow_start_with_integration_id_returns_bad_request_when_integration_inactive()
    {
        AssertSqlAvailable();
        using var authoring = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await authoring.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic60 manual start gate",
                moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var deactivate = await authoring.PostAsync($"/api/integrations/{integrationId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var launcher = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await launcher.PostAsJsonAsync(
            "/api/workflows/Sample.HelloWorld.HelloWorld/start",
            new
            {
                integrationId,
                parameters = new Dictionary<string, object?> { ["name"] = "epic60-manual" }
            });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("not active");
    }

    [Fact]
    public async Task Operation_start_with_integration_id_returns_bad_request_when_integration_inactive()
    {
        AssertSqlAvailable();
        using var authoring = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await authoring.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic60 operation start gate",
                moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var deactivate = await authoring.PostAsync($"/api/integrations/{integrationId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var launcher = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await launcher.PostAsJsonAsync(
            "/api/operations/Sample.HelloWorld.DoWork/start",
            new
            {
                integrationId
            });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("not active");
    }

    [Fact]
    public async Task Operation_start_without_integration_id_returns_bad_request()
    {
        AssertSqlAvailable();
        using var launcher = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await launcher.PostAsJsonAsync(
            "/api/operations/Sample.HelloWorld.DoWork/start",
            new
            {
                parameters = new Dictionary<string, object?> { ["message"] = "missing-integration" }
            });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("IntegrationId is required");
    }

    [Fact]
    public async Task Workflow_start_without_integration_id_returns_bad_request()
    {
        AssertSqlAvailable();
        using var launcher = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await launcher.PostAsJsonAsync(
            "/api/workflows/Sample.HelloWorld.HelloWorld/start",
            new
            {
                parameters = new Dictionary<string, object?> { ["name"] = "missing-integration" }
            });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("IntegrationId is required");
    }

    [Fact]
    public async Task Post_sftp_test_connect_persists_and_updates_readiness_across_unverified_success_failed()
    {
        AssertSqlAvailable();
        var (sftpHost, sftpPort, sftpUser, sftpPassword) = GetSftpTestSettings();
        var reachable = await IsTcpEndpointReachableAsync(sftpHost, sftpPort);
        reachable.Should().BeTrue(
            $"Epic 59A requires controlled SFTP endpoint at {sftpHost}:{sftpPort}. Set {ManagementGovernanceTestConstants.SftpHostEnvironmentVariable}/{ManagementGovernanceTestConstants.SftpPortEnvironmentVariable} and start the test SFTP container.");
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        // 1) Create integration for module with SFTP slot.
        var createIntegration = await client.PostAsJsonAsync("/api/integrations", new
        {
            displayName = "Epic59A SFTP readiness proof",
            moduleId = "Unisa.CampusNexus.PaymentPlans",
            scopeKey = "instance"
        });
        createIntegration.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdIntegrationDoc = JsonDocument.Parse(await createIntegration.Content.ReadAsStringAsync());
        var integrationId = createdIntegrationDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();

        // 2) Find SFTP-required slot.
        var detailResponse = await client.GetAsync($"/api/integrations/{integrationId}/detail");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var requiredSlots = detailDoc.RootElement.GetProperty("requiredSlots").EnumerateArray().ToArray();
        var sftpSlot = requiredSlots.FirstOrDefault(slot =>
            slot.GetProperty("contractType").GetString()?.Contains("ISftpFileClient", StringComparison.Ordinal) == true);
        sftpSlot.ValueKind.Should().NotBe(JsonValueKind.Undefined, "module should expose an SFTP slot for Epic 59A proof.");
        var slotName = sftpSlot.GetProperty("slotName").GetString();
        var contractType = sftpSlot.GetProperty("contractType").GetString();
        slotName.Should().NotBeNullOrWhiteSpace();
        contractType.Should().NotBeNullOrWhiteSpace();

        // 3) Create SFTP connection with known-good credentials.
        var createConnection = await client.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sftp",
            name = $"Epic59A SFTP {Guid.NewGuid():N}"[..24],
            config = new Dictionary<string, string>
            {
                ["Host"] = sftpHost,
                ["Port"] = sftpPort,
                ["Username"] = sftpUser
            },
            secretValues = new Dictionary<string, string>
            {
                ["Password"] = sftpPassword
            },
            scopeKey = "instance"
        });
        createConnection.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdConnectionDoc = JsonDocument.Parse(await createConnection.Content.ReadAsStringAsync());
        var connectionId = createdConnectionDoc.RootElement.GetProperty("id").GetString();
        connectionId.Should().NotBeNullOrWhiteSpace();

        // 4) Bind connection to SFTP slot.
        var bind = await client.PostAsJsonAsync($"/api/integrations/{integrationId}/bindings", new
        {
            slotName,
            contractType,
            connectionId
        });
        bind.StatusCode.Should().Be(HttpStatusCode.Created);

        // 5) Assert unverified advisory before running test-connect.
        var unverifiedDetail = await client.GetAsync($"/api/integrations/{integrationId}/detail");
        unverifiedDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var unverifiedDoc = JsonDocument.Parse(await unverifiedDetail.Content.ReadAsStringAsync());
        var unverifiedSeverity = FindIssueForConnection(
            unverifiedDoc.RootElement.GetProperty("readiness"),
            "connection_unverified",
            connectionId!);
        unverifiedSeverity.Should().Be("Advisory");

        // 6) Test-connect success.
        var testConnectSuccess = await client.PostAsync($"/api/connections/{connectionId}/test-connect", null);
        testConnectSuccess.StatusCode.Should().Be(HttpStatusCode.OK);
        using var successDoc = JsonDocument.Parse(await testConnectSuccess.Content.ReadAsStringAsync());
        successDoc.RootElement.GetProperty("supportsVerification").GetBoolean().Should().BeTrue();
        successDoc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Success");
        successDoc.RootElement.GetProperty("lastVerificationAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        successDoc.RootElement.GetProperty("lastVerificationSummary").GetString().Should().NotBeNullOrWhiteSpace();

        // 7) Assert persisted projection from fresh GET.
        var getAfterSuccess = await client.GetAsync($"/api/connections/{connectionId}");
        getAfterSuccess.StatusCode.Should().Be(HttpStatusCode.OK);
        using var persistedSuccessDoc = JsonDocument.Parse(await getAfterSuccess.Content.ReadAsStringAsync());
        persistedSuccessDoc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Success");
        persistedSuccessDoc.RootElement.GetProperty("lastVerificationAtUtc").GetString().Should().NotBeNullOrWhiteSpace();

        // 8) Success should clear verification advisories for this bound connection.
        var successDetail = await client.GetAsync($"/api/integrations/{integrationId}/detail");
        successDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var successDetailDoc = JsonDocument.Parse(await successDetail.Content.ReadAsStringAsync());
        FindIssueForConnection(successDetailDoc.RootElement.GetProperty("readiness"), "connection_unverified", connectionId!)
            .Should().BeNull();
        FindIssueForConnection(successDetailDoc.RootElement.GetProperty("readiness"), "connection_verification_failed", connectionId!)
            .Should().BeNull();

        // 9) Update connection to a known-bad password and verify failure path.
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/connections/{connectionId}")
        {
            Content = JsonContent.Create(new
            {
                secretValues = new Dictionary<string, string>
                {
                    ["Password"] = "wrong-password-epic59a"
                }
            })
        };
        var patchResponse = await client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var testConnectFailed = await client.PostAsync($"/api/connections/{connectionId}/test-connect", null);
        testConnectFailed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var failedDoc = JsonDocument.Parse(await testConnectFailed.Content.ReadAsStringAsync());
        failedDoc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Failed");
        failedDoc.RootElement.GetProperty("lastVerificationAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        failedDoc.RootElement.GetProperty("lastVerificationSummary").GetString().Should().NotBeNullOrWhiteSpace();

        var getAfterFailure = await client.GetAsync($"/api/connections/{connectionId}");
        getAfterFailure.StatusCode.Should().Be(HttpStatusCode.OK);
        using var persistedFailureDoc = JsonDocument.Parse(await getAfterFailure.Content.ReadAsStringAsync());
        persistedFailureDoc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Failed");
        persistedFailureDoc.RootElement.GetProperty("lastVerificationSummary").GetString().Should().NotBeNullOrWhiteSpace();

        // 10) Failure should surface deterministic advisory readiness issue.
        var failedDetail = await client.GetAsync($"/api/integrations/{integrationId}/detail");
        failedDetail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var failedDetailDoc = JsonDocument.Parse(await failedDetail.Content.ReadAsStringAsync());
        var failedSeverity = FindIssueForConnection(
            failedDetailDoc.RootElement.GetProperty("readiness"),
            "connection_verification_failed",
            connectionId!);
        failedSeverity.Should().Be("Advisory");
    }

    [Fact]
    public async Task Get_integration_procedures_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_integration_procedures_with_integration_read_returns_200_with_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        var isRunnable = readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean();

        var response = await client.GetAsync($"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("integrationReady", out var ready).Should().BeTrue();
        ready.GetBoolean().Should().Be(isRunnable);
        var procedures = root.GetProperty("procedures");
        procedures.ValueKind.Should().Be(JsonValueKind.Array);
        procedures.GetArrayLength().Should().BeGreaterThan(0, "HelloWorld module contributes at least one procedure in the governance host.");
        foreach (var p in procedures.EnumerateArray())
        {
            p.TryGetProperty("launchable", out var launchable).Should().BeTrue();
            p.TryGetProperty("blockedReasonCodes", out var blocked).Should().BeTrue();
            blocked.ValueKind.Should().Be(JsonValueKind.Array);
            p.TryGetProperty("launchKind", out var launchKind).Should().BeTrue();
            var kind = launchKind.GetString();
            if (!isRunnable)
            {
                launchable.GetBoolean().Should().BeFalse();
                blocked.EnumerateArray().Select(x => x.GetString()).Should().Contain("integration_hard_blocked");
            }
            else
            {
                // Integration reader lacks workflow / operation start rights; list stays truthful (Epic 56A/56B).
                launchable.GetBoolean().Should().BeFalse();
                if (string.Equals(kind, "workflow", StringComparison.Ordinal))
                {
                    blocked.EnumerateArray().Select(x => x.GetString()).Should().Contain("insufficient_workflow_permission");
                }
                else
                {
                    string.Equals(kind, "operation", StringComparison.Ordinal).Should().BeTrue("expected workflow or operation procedure from HelloWorld");
                    blocked.EnumerateArray().Select(x => x.GetString()).Should().Contain("insufficient_operation_permission");
                }
            }
        }
    }

    [Fact]
    public async Task Get_integration_procedures_unknown_integration_returns_404()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync("/api/integrations/intgovernmissing00000001/procedures");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_integration_procedure_detail_unknown_procedure_returns_404()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/no.such.procedure.id.here");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_integration_procedure_launch_integration_reader_forbidden_or_conflict_when_not_ready()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        var isRunnable = readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean();

        using var content = new StringContent("""{"parameters":{"name":"t"}}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.greet/launch",
            content);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!isRunnable)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            doc.RootElement.GetProperty("code").GetString().Should().Be("integration_hard_blocked");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            doc.RootElement.GetProperty("code").GetString().Should().Be("workflow_start_forbidden");
        }
    }

    [Fact]
    public async Task Post_integration_procedure_launch_procedure_launcher_invalid_parameters_returns_400()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.greet/launch",
            new { parameters = new Dictionary<string, object?>() });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("invalid_parameters");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_operation_invalid_parameters_returns_400()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?>() });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("invalid_parameters");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_unknown_integration_returns_404()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await client.PostAsJsonAsync(
            "/api/integrations/intgovernmissing00000001/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "x" } });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("integration_not_found");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_procedure_wrong_module_returns_404()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationForeignModuleId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "x" } });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("procedure_wrong_module");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_workflow_only_principal_operation_forbidden_when_runnable()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalWorkflowOnlyProcedureLauncher);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        if (!readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean())
            return;

        var response = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "from-test" } });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("operation_start_forbidden");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_operation_conflict_when_not_ready()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        if (readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean())
            return;

        var response = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "x" } });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().Be("integration_hard_blocked");
    }

    [Fact]
    public async Task Procedure_launcher_start_authority_is_known_and_granted_before_launch()
    {
        AssertSqlAvailable();
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();

        var link = await db.IdentityLinks.SingleOrDefaultAsync(l =>
            l.ExternalId == ManagementGovernanceTestConstants.ExternalProcedureLauncher &&
            l.Source == (int)IdentitySource.Local);
        link.Should().NotBeNull("the launcher external id must resolve to a local user");
        link!.UserId.Should().Be(ManagementGovernanceTestConstants.UserProcedureLauncher);

        var membership = await db.UserRoles.SingleOrDefaultAsync(ur =>
            ur.UserId == link.UserId && ur.RoleId == ManagementGovernanceTestConstants.RoleProcedureLauncher);
        membership.Should().NotBeNull("the launcher user must hold rolgovernproclaunch00001");

        var role = await db.Roles.SingleAsync(r => r.Id == ManagementGovernanceTestConstants.RoleProcedureLauncher);
        role.PermissionsJson.Should().Contain("\"action\":\"workflow.execute\"");
        role.PermissionsJson.Should().Contain("\"resourceType\":\"WorkflowInstance\"");
        role.PermissionsJson.Should().Contain("\"scopeKey\":\"module:Sample.HelloWorld\"");
        role.PermissionsJson.Should().Contain("\"action\":\"operation.execute\"");
        role.PermissionsJson.Should().Contain("\"resourceType\":\"OperationDefinition\"");
        role.PermissionsJson.Should().Contain("\"action\":\"integration.read\"");

        var environmentId = scope.ServiceProvider.GetRequiredService<IEnvironmentContext>().EnvironmentId;
        var install = await db.ModuleInstallationStates.SingleOrDefaultAsync(m =>
            m.EnvironmentId == environmentId && m.ModuleId == ManagementGovernanceTestConstants.HelloWorldModuleId);
        install.Should().NotBeNull($"Sample.HelloWorld must be installed for environment '{environmentId}'");
        install!.Enabled.Should().BeTrue();

        var catalog = scope.ServiceProvider.GetRequiredService<IResourceDescriptorRegistry>().GetPermissionDescriptors(null);
        catalog.Should().Contain(p =>
            p.Action == "workflow.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == "WorkflowInstance" &&
            p.Resource.ScopeKey == "module:Sample.HelloWorld",
            "IsKnown fails closed when this catalog entry was not registered at host start");
        catalog.Should().Contain(p =>
            p.Action == "operation.execute" &&
            p.Resource != null &&
            p.Resource.ResourceType == "OperationDefinition" &&
            p.Resource.ScopeKey == "module:Sample.HelloWorld",
            "IsKnown fails closed when this catalog entry was not registered at host start");

        var definition = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionResolver>()
            .GetByIdAsync(new WorkflowDefinitionId("Sample.HelloWorld.HelloWorld"));
        definition.Should().NotBeNull("the activation filter must see the enabled HelloWorld install");
        var workflowPermission = definition!.RequiredPermissions.Should().ContainSingle().Subject;
        workflowPermission.Action.Should().Be("workflow.execute");
        workflowPermission.Resource!.ResourceType.Should().Be("WorkflowInstance");
        workflowPermission.Resource.ScopeKey.Should().Be("module:Sample.HelloWorld");

        var operation = await scope.ServiceProvider.GetRequiredService<IOperationRegistryService>()
            .GetOperationAsync(new OperationId("Sample.HelloWorld.DoWork"));
        operation.Should().NotBeNull();
        var operationPermission = operation!.RequiredPermissions.Should().ContainSingle().Subject;
        operationPermission.Action.Should().Be("operation.execute");
        operationPermission.Resource!.ResourceType.Should().Be("OperationDefinition");
        operationPermission.Resource.ScopeKey.Should().Be("module:Sample.HelloWorld");

        // A local session's actor is the user id (IAM-9.2), not the sign-in name.
        var actor = new HumanActor(
            new ActorId(ManagementGovernanceTestConstants.UserProcedureLauncher),
            preferredIdentitySource: IdentitySource.Local);
        var evaluator = scope.ServiceProvider.GetRequiredService<IExecutablePermissionEvaluator>();
        (await evaluator.EvaluateAllAsync(actor, definition.RequiredPermissions!)).Should().BeTrue(
            "catalog, user resolution, role membership, grant, and scope must all allow workflow start");
        (await evaluator.EvaluateAllAsync(actor, operation.RequiredPermissions)).Should().BeTrue(
            "catalog, user resolution, role membership, grant, and scope must all allow operation start");
    }

    [Fact]
    public async Task Post_integration_procedure_launch_workflow_and_operation_accepted_when_runnable()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        if (!readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean())
            return;

        var wf = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.greet/launch",
            new { parameters = new Dictionary<string, object?> { ["name"] = "Epic56B" } });
        wf.StatusCode.Should().Be(HttpStatusCode.Accepted, await wf.Content.ReadAsStringAsync());
        var wfJson = await wf.Content.ReadAsStringAsync();
        using (var wfDoc = JsonDocument.Parse(wfJson))
        {
            wfDoc.RootElement.GetProperty("launchKind").GetString().Should().Be("workflow");
            wfDoc.RootElement.GetProperty("workflowInstanceId").GetString().Should().NotBeNullOrEmpty();
        }

        var op = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "Epic56B-op" } });
        op.StatusCode.Should().Be(HttpStatusCode.Accepted, await op.Content.ReadAsStringAsync());
        var opJson = await op.Content.ReadAsStringAsync();
        using (var opDoc = JsonDocument.Parse(opJson))
        {
            opDoc.RootElement.GetProperty("launchKind").GetString().Should().Be("operation");
            opDoc.RootElement.GetProperty("operationInstanceId").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Get_run_after_procedure_launch_shows_provenance_and_start_parameters_when_runnable()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var readinessResponse = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/readiness");
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var readinessDoc = JsonDocument.Parse(readinessJson);
        if (!readinessDoc.RootElement.GetProperty("isRunnable").GetBoolean())
            return;

        const string wfParamValue = "Epic56C-wf-provenance";
        var wf = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.greet/launch",
            new { parameters = new Dictionary<string, object?> { ["name"] = wfParamValue } });
        wf.StatusCode.Should().Be(HttpStatusCode.Accepted, await wf.Content.ReadAsStringAsync());
        string? wfRunId;
        using (var launchDoc = JsonDocument.Parse(await wf.Content.ReadAsStringAsync()))
        {
            wfRunId = launchDoc.RootElement.GetProperty("runId").GetString();
            launchDoc.RootElement.GetProperty("launchKind").GetString().Should().Be("workflow");
        }

        var wfDetailResponse = await client.GetAsync($"/api/runs/{wfRunId}");
        wfDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var wfDetailJson = await wfDetailResponse.Content.ReadAsStringAsync();
        using (var wfDetail = JsonDocument.Parse(wfDetailJson))
        {
            wfDetail.RootElement.GetProperty("executionKind").GetString().Should().Be("workflow");
            wfDetail.RootElement.GetProperty("integrationId").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
            wfDetail.RootElement.GetProperty("procedureId").GetString().Should().Be("Sample.HelloWorld.procedures.greet");
            var startJson = wfDetail.RootElement.GetProperty("startParametersJson").GetString();
            startJson.Should().NotBeNullOrWhiteSpace();
            startJson.Should().Contain(wfParamValue);
        }

        const string opParamValue = "Epic56C-op-provenance";
        var op = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = opParamValue } });
        op.StatusCode.Should().Be(HttpStatusCode.Accepted, await op.Content.ReadAsStringAsync());
        string? opRunId;
        using (var opLaunchDoc = JsonDocument.Parse(await op.Content.ReadAsStringAsync()))
        {
            opRunId = opLaunchDoc.RootElement.GetProperty("runId").GetString();
            opLaunchDoc.RootElement.GetProperty("launchKind").GetString().Should().Be("operation");
        }

        var opDetailResponse = await client.GetAsync($"/api/runs/{opRunId}");
        opDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var opDetailJson = await opDetailResponse.Content.ReadAsStringAsync();
        using (var opDetail = JsonDocument.Parse(opDetailJson))
        {
            opDetail.RootElement.GetProperty("executionKind").GetString().Should().Be("operation");
            opDetail.RootElement.GetProperty("integrationId").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
            opDetail.RootElement.GetProperty("procedureId").GetString().Should().Be("Sample.HelloWorld.procedures.opPing");
            var opStartJson = opDetail.RootElement.GetProperty("startParametersJson").GetString();
            opStartJson.Should().NotBeNullOrWhiteSpace();
            opStartJson.Should().Contain(opParamValue);
        }

        var listResponse = await client.GetAsync("/api/runs?executionKind=operation");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        listDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        var opRow = listDoc.RootElement.EnumerateArray().FirstOrDefault(e =>
            string.Equals(e.GetProperty("runId").GetString(), opRunId, StringComparison.Ordinal));
        opRow.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        opRow.GetProperty("integrationId").GetString().Should().Be(ManagementGovernanceTestConstants.SeededIntegrationId);
        opRow.GetProperty("procedureId").GetString().Should().Be("Sample.HelloWorld.procedures.opPing");
    }

    [Fact]
    public async Task Get_integration_procedure_detail_operation_returns_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("launchKind").GetString().Should().Be("operation");
        doc.RootElement.GetProperty("targetDefinitionId").GetString().Should().Be("Sample.HelloWorld.DoWork");
        doc.RootElement.GetProperty("parameterFields").ValueKind.Should().Be(JsonValueKind.Array);
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

    [Fact]
    public async Task Get_integrations_fleet_returns_rows_with_readiness_and_attention_shape()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var response = await client.GetAsync("/api/integrations/fleet");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        var seeded = doc.RootElement.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("id").GetString() == ManagementGovernanceTestConstants.SeededIntegrationId);
        seeded.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        seeded.GetProperty("operationalState").GetString().Should().NotBeNullOrWhiteSpace();
        var readiness = seeded.GetProperty("readiness");
        var runnableKind = readiness.GetProperty("isRunnable").ValueKind;
        (runnableKind == JsonValueKind.True || runnableKind == JsonValueKind.False).Should().BeTrue();
        readiness.GetProperty("blockingIssueCount").TryGetInt32(out _).Should().BeTrue();
        readiness.GetProperty("advisoryIssueCount").TryGetInt32(out _).Should().BeTrue();
        seeded.GetProperty("attention").GetProperty("hasConfigBlockingReadiness").ValueKind
            .Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        seeded.GetProperty("attentionTokens").ValueKind.Should().Be(JsonValueKind.Array);
        seeded.GetProperty("topIssueCodes").ValueKind.Should().Be(JsonValueKind.Array);
        var ops = seeded.GetProperty("operationalRuns");
        ops.GetProperty("recentFailedRunCount").TryGetInt32(out _).Should().BeTrue();
        ops.GetProperty("hasRecentOperationalFailure").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        ops.GetProperty("hasRepeatedRecentFailures").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        ops.GetProperty("mostRecentAuthorizedRunFailed").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        var expected = seeded.GetProperty("expectedRuns");
        expected.GetProperty("hasRecurringScheduleExpectation").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        expected.GetProperty("hasUnexpectedSilence").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        expected.GetProperty("lastScheduleOriginAuthorizedRunId").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        expected.GetProperty("lastScheduleOriginAuthorizedRunAt").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        expected.GetProperty("expectedRunEvaluationStatus").ValueKind.Should().Be(JsonValueKind.String);
        expected.GetProperty("lastExpectedFireUtc").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        expected.GetProperty("expectedFireCountInWindow").TryGetInt32(out _).Should().BeTrue();
        expected.GetProperty("expectedRunSatisfied").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        expected.GetProperty("expectedRunInGracePeriod").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        expected.GetProperty("operatorSummaryLine").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        expected.GetProperty("expectedRunGraceReason").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        expected.GetProperty("expectedRunGraceAnchorUtc").ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_runs_with_integrationId_query_returns_ok_array()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var response = await client.GetAsync(
            $"/api/runs?integrationId={Uri.EscapeDataString(ManagementGovernanceTestConstants.SeededIntegrationId)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_integrations_fleet_without_integration_read_returns_403()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);
        var response = await client.GetAsync("/api/integrations/fleet");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_integrations_fleet_state_inactive_includes_deactivated_integration_only_in_that_filter()
    {
        AssertSqlAvailable();
        using var authoring = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var createResponse = await authoring.PostAsJsonAsync(
            "/api/integrations",
            new
            {
                displayName = "Epic61 fleet inactive filter",
                moduleId = ManagementGovernanceTestConstants.HelloWorldModuleId,
                scopeKey = "instance"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var integrationId = createdDoc.RootElement.GetProperty("id").GetString();
        integrationId.Should().NotBeNullOrWhiteSpace();
        var deactivate = await authoring.PostAsync($"/api/integrations/{integrationId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var reader = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);
        var fleetInactive = await reader.GetAsync("/api/integrations/fleet?state=Inactive");
        fleetInactive.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await fleetInactive.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateArray().Any(e => e.GetProperty("id").GetString() == integrationId).Should().BeTrue();
        doc.RootElement.EnumerateArray().Any(e => e.GetProperty("id").GetString() == ManagementGovernanceTestConstants.SeededIntegrationId).Should().BeFalse();
    }
}
