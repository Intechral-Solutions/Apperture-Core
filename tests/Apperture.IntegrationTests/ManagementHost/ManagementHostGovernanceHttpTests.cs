using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
        first.GetProperty("capabilities").GetString().Should().NotBeNullOrEmpty();
        first.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.ValueKind.Should().Be(JsonValueKind.Array);
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
                secretRefs = new Dictionary<string, string>
                {
                    ["Password"] = "local:test/password"
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
                blocked.EnumerateArray().Select(x => x.GetString()).Should().Contain("integration_not_ready");
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
            doc.RootElement.GetProperty("code").GetString().Should().Be("integration_not_ready");
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
        doc.RootElement.GetProperty("code").GetString().Should().Be("integration_not_ready");
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
        wf.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var wfJson = await wf.Content.ReadAsStringAsync();
        using (var wfDoc = JsonDocument.Parse(wfJson))
        {
            wfDoc.RootElement.GetProperty("launchKind").GetString().Should().Be("workflow");
            wfDoc.RootElement.GetProperty("workflowInstanceId").GetString().Should().NotBeNullOrEmpty();
        }

        var op = await client.PostAsJsonAsync(
            $"/api/integrations/{ManagementGovernanceTestConstants.SeededIntegrationId}/procedures/Sample.HelloWorld.procedures.opPing/launch",
            new { parameters = new Dictionary<string, object?> { ["message"] = "Epic56B-op" } });
        op.StatusCode.Should().Be(HttpStatusCode.Accepted);
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
        wf.StatusCode.Should().Be(HttpStatusCode.Accepted);
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
        op.StatusCode.Should().Be(HttpStatusCode.Accepted);
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
}
