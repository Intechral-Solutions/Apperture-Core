using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// SEC-2.1 through the real Management host: acknowledging an attention incident needs <c>integration.update</c>, a principal that
/// may only read the integration is refused with 403 and nothing changes, an operator who may update it acknowledges once and the
/// route answers 204 on a repeat without a second audit row, a resolved incident is a 409 with a stable code, and an unknown
/// incident is a 404. Each case works on an integration of its own (a copy of the seeded one, removed afterwards), so an open
/// incident cannot change the readiness of the integration other cases use. Fails, never skips, without SQL Server.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostAttentionIncidentAcknowledgementHttpTests : IAsyncLifetime
{
    private const string NoteSentinel = "http-note-sentinel-do-not-record";
    private const int NoteMaxLength = 1024;

    private readonly ManagementHostGovernanceFixture _fixture;
    private readonly List<string> _integrations = [];

    public ManagementHostAttentionIncidentAcknowledgementHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_integrations.Count == 0 || !_fixture.SqlAvailable)
            return;
        await using var connection = await OpenAsync();
        foreach (var id in _integrations)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM AttentionIncidents WHERE IntegrationId = @id; DELETE FROM Integrations WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private void AssertSqlAvailable() =>
        Assert.True(
            _fixture.SqlAvailable,
            $"These tests require SQL Server or LocalDB. {SqlServerTestConfiguration.DescribeActiveConfiguration()} " +
            $"Connection attempted: {SqlServerTestConfiguration.DescribeConnectionSummary(_fixture.ConnectionString)}");

    private static string Route(string integrationId, string incidentId) =>
        $"/api/integrations/{integrationId}/attention-incidents/{incidentId}/acknowledge";

    // ---- who may acknowledge ----

    [Fact]
    public async Task A_principal_with_only_integration_read_is_forbidden_and_the_incident_and_audit_table_are_untouched()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync();
        var before = await IncidentTextAsync(incidentId);
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationReader);

        var response = await client.PostAsJsonAsync(Route(integrationId, incidentId), new { note = NoteSentinel });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IncidentTextAsync(incidentId)).Should().Be(before, "a refused acknowledgement changes no column");
        (await AuditRowCountAsync(incidentId)).Should().Be(0);
    }

    [Fact]
    public async Task A_principal_that_may_not_even_read_the_integration_is_forbidden()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync();
        var before = await IncidentTextAsync(incidentId);
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalAuthNoPerms);

        var response = await client.PostAsJsonAsync(Route(integrationId, incidentId), new { note = NoteSentinel });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IncidentTextAsync(incidentId)).Should().Be(before);
        (await AuditRowCountAsync(incidentId)).Should().Be(0);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_unauthorized_and_changes_nothing()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync();
        var before = await IncidentTextAsync(incidentId);

        var response = await _fixture.CreateClient().PostAsJsonAsync(Route(integrationId, incidentId), new { note = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IncidentTextAsync(incidentId)).Should().Be(before);
        (await AuditRowCountAsync(incidentId)).Should().Be(0);
    }

    // ---- an operator who may update ----

    [Fact]
    public async Task An_operator_with_integration_update_acknowledges_once_and_a_repeat_is_a_204_that_changes_and_records_nothing()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        var first = await client.PostAsJsonAsync(Route(integrationId, incidentId), new { note = "  " + NoteSentinel + "  " });

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var acknowledged = await IncidentTextAsync(incidentId);
        acknowledged.Should().Contain(ManagementGovernanceTestConstants.UserIntegrationAuthoring, "the acknowledger is the authenticated user");
        acknowledged.Should().Contain(NoteSentinel);
        (await AuditRowCountAsync(incidentId)).Should().Be(1);
        var audit = await AuditRowAsync(incidentId);
        audit.Actor.Should().Be(ManagementGovernanceTestConstants.UserIntegrationAuthoring);
        audit.TargetId.Should().Be(integrationId);
        audit.Details.Should().NotContain(NoteSentinel, "the note is never recorded");
        using (var details = JsonDocument.Parse(audit.Details))
            details.RootElement.GetProperty("noteProvided").GetBoolean().Should().BeTrue();

        var second = await client.PostAsJsonAsync(Route(integrationId, incidentId), new { note = "a different note" });

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IncidentTextAsync(incidentId)).Should().Be(acknowledged, "the first acknowledger, time and note are kept");
        (await AuditRowCountAsync(incidentId)).Should().Be(1, "the repeat records nothing");
    }

    [Fact]
    public async Task A_resolved_incident_is_a_409_with_a_stable_code_and_nothing_is_written()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync(resolved: true);
        var before = await IncidentTextAsync(incidentId);
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        var response = await client.PostAsJsonAsync(Route(integrationId, incidentId), new { note = NoteSentinel });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("attention_incident_not_active");
        (await IncidentTextAsync(incidentId)).Should().Be(before);
        (await AuditRowCountAsync(incidentId)).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_incident_an_incident_of_another_integration_and_an_unknown_integration_are_404()
    {
        AssertSqlAvailable();
        var (integrationId, incidentId) = await SeedIncidentAsync();
        var (otherIntegrationId, _) = await SeedIncidentAsync();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        (await client.PostAsJsonAsync(Route(integrationId, "attn_does_not_exist"), new { note = "x" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(Route(otherIntegrationId, incidentId), new { note = "x" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the incident belongs to another integration");
        (await client.PostAsJsonAsync(Route("int-does-not-exist", "attn_x"), new { note = "x" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await AuditRowCountAsync(incidentId)).Should().Be(0);
    }

    [Fact]
    public async Task A_note_over_1024_characters_is_a_400_and_a_blank_note_is_stored_as_null()
    {
        AssertSqlAvailable();
        var (integrationId, tooLong) = await SeedIncidentAsync();
        var (blankIntegrationId, blank) = await SeedIncidentAsync();
        using var client = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);

        var refused = await client.PostAsJsonAsync(Route(integrationId, tooLong), new { note = new string('n', NoteMaxLength + 1) });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AuditRowCountAsync(tooLong)).Should().Be(0);

        var accepted = await client.PostAsJsonAsync(Route(blankIntegrationId, blank), new { note = "   " });
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NoteOfAsync(blank)).Should().BeNull();
    }

    // ---- the database ----

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A copy of the seeded integration (so the same principals may act on it) with one incident, straight into the tables.</summary>
    private async Task<(string IntegrationId, string IncidentId)> SeedIncidentAsync(bool resolved = false)
    {
        var integrationId = "int-http-" + Guid.NewGuid().ToString("N")[..12];
        var incidentId = "attn_" + Guid.NewGuid().ToString("N");
        await using var connection = await OpenAsync();

        await using (var copy = connection.CreateCommand())
        {
            copy.CommandText =
                """
                INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, SettingsJson, CreatedAt, UpdatedAt)
                SELECT @id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM Integrations WHERE Id = @seed;
                """;
            copy.Parameters.AddWithValue("@id", integrationId);
            copy.Parameters.AddWithValue("@seed", ManagementGovernanceTestConstants.SeededIntegrationId);
            (await copy.ExecuteNonQueryAsync()).Should().Be(1, "the seeded integration is copied");
        }

        _integrations.Add(integrationId);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AttentionIncidents (Id, IntegrationId, ConditionKey, Status, StartedAtUtc, LastObservedAtUtc, ResolvedAtUtc, ResolutionReason)
            VALUES (@id, @integration, @key, @status, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
                    CASE WHEN @resolved = 1 THEN SYSDATETIMEOFFSET() END, CASE WHEN @resolved = 1 THEN @reason END);
            """;
        command.Parameters.AddWithValue("@id", incidentId);
        command.Parameters.AddWithValue("@integration", integrationId);
        command.Parameters.AddWithValue("@key", AttentionConditionKeys.ExpectedRunMissing);
        command.Parameters.AddWithValue("@status", resolved ? AttentionIncidentStatuses.Resolved : AttentionIncidentStatuses.Active);
        command.Parameters.AddWithValue("@resolved", resolved ? 1 : 0);
        command.Parameters.AddWithValue("@reason", AttentionIncidentResolutionReasons.ExpectationCleared);
        await command.ExecuteNonQueryAsync();
        return (integrationId, incidentId);
    }

    private async Task<string> IncidentTextAsync(string incidentId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONCAT(Status, '|', AcknowledgedAtUtc, '|', AcknowledgedBy, '|', AcknowledgementNote) FROM AttentionIncidents WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", incidentId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> NoteOfAsync(string incidentId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AcknowledgementNote FROM AttentionIncidents WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", incidentId);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : (string?)value;
    }

    private async Task<int> AuditRowCountAsync(string incidentId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE Action = @action AND Details LIKE @like;";
        command.Parameters.AddWithValue("@action", AuditActionNames.AttentionIncidentAcknowledged);
        command.Parameters.AddWithValue("@like", $"%{incidentId}%");
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(string Actor, string TargetId, string Details)> AuditRowAsync(string incidentId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Actor, TargetId, Details FROM AuditLogs WHERE Action = @action AND Details LIKE @like;";
        command.Parameters.AddWithValue("@action", AuditActionNames.AttentionIncidentAcknowledged);
        command.Parameters.AddWithValue("@like", $"%{incidentId}%");
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}
