using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// SEC-2.2 through the real Management host (A2.8, A2.9): no connection response, whether list, detail, create, update or
/// test-connect, carries a secret reference; the read model is <c>secretFields</c>; a PATCH that sends <c>secretRefs</c> is refused
/// with a 400 and changes nothing; a poisoned field is refused by test-connect with the fixed message and one audit row, and is
/// repaired by <c>secretValues</c> without the foreign secret's descriptor or versions changing; and an undeclared legacy field is
/// cleared by <c>clearSecretFields</c>. The tables these cases write (a connection and its secrets) are removed afterwards; the audit
/// table is append-only. Fails, never skips, without SQL Server.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostConnectionSecretFieldsHttpTests : IAsyncLifetime
{
    private const string PasswordSentinel = "http-password-sentinel-do-not-return";
    private const string NewPasswordSentinel = "http-new-password-sentinel";
    private const string Operator = ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator;

    private readonly ManagementHostGovernanceFixture _fixture;
    private readonly List<string> _connections = [];

    public ManagementHostConnectionSecretFieldsHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_connections.Count == 0 || !_fixture.SqlAvailable)
            return;
        await using var connection = await OpenAsync();
        foreach (var id in _connections)
        {
            // The secrets these connections own (by name) go with them; the audit rows stay, as they must.
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM SecretVersions WHERE SecretId IN (SELECT Id FROM SecretDescriptors WHERE Name LIKE @pattern);
                DELETE FROM SecretAudits WHERE SecretId IN (SELECT Id FROM SecretDescriptors WHERE Name LIKE @pattern);
                DELETE FROM SecretDescriptors WHERE Name LIKE @pattern;
                DELETE FROM Connections WHERE Id = @id;
                """;
            command.Parameters.AddWithValue("@pattern", $"connection:{id}:%");
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private void AssertSqlAvailable() =>
        Assert.True(
            _fixture.SqlAvailable,
            $"These tests require SQL Server or LocalDB. {SqlServerTestConfiguration.DescribeActiveConfiguration()} " +
            $"Connection attempted: {SqlServerTestConfiguration.DescribeConnectionSummary(_fixture.ConnectionString)}");

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    // ---- arranging ----

    /// <summary>A SQL connection with a stored Password, created through the API so the secret is the connection's own.</summary>
    private async Task<string> CreateConnectionAsync(HttpClient client, string password = PasswordSentinel)
    {
        var create = await client.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sql",
            name = $"SEC22 {Guid.NewGuid():N}"[..20],
            config = new Dictionary<string, string> { ["Server"] = "127.0.0.1,1", ["Database"] = "Target", ["UserId"] = "reader" },
            secretRefs = new Dictionary<string, string>(),
            secretValues = new Dictionary<string, string> { ["Password"] = password },
            scopeKey = "instance",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        _connections.Add(id);
        return id;
    }

    private async Task<string> StoredRefsAsync(string connectionId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SecretRefsJson FROM Connections WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", connectionId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task SetStoredRefsAsync(string connectionId, string json)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Connections SET SecretRefsJson = @json WHERE Id = @id;";
        command.Parameters.AddWithValue("@json", json);
        command.Parameters.AddWithValue("@id", connectionId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task<string> ReferenceOfAsync(string connectionId, string field = "Password")
    {
        using var doc = JsonDocument.Parse(await StoredRefsAsync(connectionId));
        return doc.RootElement.GetProperty(field).GetString()!;
    }

    /// <summary>Everything the secret store holds about one secret, as one comparable text (the payload as base64).</summary>
    private async Task<string> SecretSnapshotAsync(string secretId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CONCAT(d.Name, '|', d.Provider, '|', d.State, '|', d.ActiveVersion, '|', d.UpdatedAt, '#',
                   (SELECT STRING_AGG(CONCAT(v.Version, ':', v.State, ':', CONVERT(varchar(max), v.ProtectedPayload, 1)), ',') FROM SecretVersions v WHERE v.SecretId = d.Id), '#',
                   (SELECT COUNT(*) FROM SecretAudits a WHERE a.SecretId = d.Id))
            FROM SecretDescriptors d WHERE d.Id = @id;
            """;
        command.Parameters.AddWithValue("@id", Guid.Parse(secretId));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<(string Actor, string Details)>> RefusalRowsAsync(string connectionId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Actor, Details FROM AuditLogs WHERE Action = @action AND TargetId = @id ORDER BY Id;";
        command.Parameters.AddWithValue("@action", AuditActionNames.ConnectionSecretResolutionRefused);
        command.Parameters.AddWithValue("@id", connectionId);
        var rows = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private static JsonElement SecretFieldOf(JsonElement connection, string key) =>
        connection.GetProperty("secretFields").EnumerateArray().Single(f => f.GetProperty("key").GetString() == key);

    // ---- A2.8: no response carries a reference ----

    [Fact]
    public async Task No_connection_response_carries_a_secret_reference_and_each_reports_secretFields()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var id = await CreateConnectionAsync(client);
        var reference = await ReferenceOfAsync(id);
        var bodies = new Dictionary<string, string>();

        var create = await client.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sql",
            name = $"SEC22 {Guid.NewGuid():N}"[..20],
            config = new Dictionary<string, string> { ["Server"] = "127.0.0.1,1", ["Database"] = "Target" },
            secretRefs = new Dictionary<string, string>(),
            secretValues = new Dictionary<string, string> { ["Password"] = PasswordSentinel },
            scopeKey = "instance",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        bodies["create"] = await create.Content.ReadAsStringAsync();
        _connections.Add(JsonDocument.Parse(bodies["create"]).RootElement.GetProperty("id").GetString()!);
        var createdReference = await ReferenceOfAsync(_connections[^1]);

        var get = await client.GetAsync($"/api/connections/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies["get"] = await get.Content.ReadAsStringAsync();
        var list = await client.GetAsync("/api/connections");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies["list"] = await list.Content.ReadAsStringAsync();
        var patch = await client.PatchAsJsonAsync($"/api/connections/{id}", new { name = "SEC22 renamed" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies["patch"] = await patch.Content.ReadAsStringAsync();
        var rotate = await client.PatchAsJsonAsync($"/api/connections/{id}", new { secretValues = new { Password = NewPasswordSentinel } });
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies["rotate"] = await rotate.Content.ReadAsStringAsync();
        var verify = await client.PostAsync($"/api/connections/{id}/test-connect", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies["test-connect"] = await verify.Content.ReadAsStringAsync();

        foreach (var (name, body) in bodies)
        {
            body.Should().NotContain(reference, $"{name}: the stored reference is never serialized")
                .And.NotContain(createdReference).And.NotContain(reference.Replace("-", string.Empty))
                .And.NotContain(PasswordSentinel).And.NotContain(NewPasswordSentinel)
                .And.NotContainEquivalentOf("secretRefs", $"{name}: the property is gone, not renamed or emptied");
            body.Should().Contain("\"secretFields\"");
        }

        using var detail = JsonDocument.Parse(bodies["get"]);
        SecretFieldOf(detail.RootElement, "Password").GetProperty("configured").GetBoolean().Should().BeTrue();
        SecretFieldOf(detail.RootElement, "Password").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("key", "configured");
        using var listDoc = JsonDocument.Parse(bodies["list"]);
        foreach (var item in listDoc.RootElement.EnumerateArray())
        {
            item.TryGetProperty("secretFields", out var fields).Should().BeTrue();
            fields.ValueKind.Should().Be(JsonValueKind.Array);
            item.TryGetProperty("secretRefs", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_declared_secret_field_with_nothing_stored_is_listed_as_not_configured_and_a_legacy_key_is_listed_by_its_key()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var id = await CreateConnectionAsync(client);
        await SetStoredRefsAsync(id, "{\"OldApiKey\":\"" + Guid.NewGuid() + "\",\"BlankLegacy\":\"  \"}");

        var response = await client.GetAsync($"/api/connections/{id}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        SecretFieldOf(doc.RootElement, "Password").GetProperty("configured").GetBoolean().Should().BeFalse("the connector declares it and nothing is stored");
        SecretFieldOf(doc.RootElement, "OldApiKey").GetProperty("configured").GetBoolean().Should().BeTrue();
        SecretFieldOf(doc.RootElement, "BlankLegacy").GetProperty("configured").GetBoolean().Should().BeFalse("a blank reference is not configured");
    }

    // ---- A2.9: the retired write contract ----

    [Theory]
    [InlineData("{\"secretRefs\":{\"Password\":\"00000000-0000-0000-0000-000000000001\"}}")]
    [InlineData("{\"secretRefs\":{}}")]
    [InlineData("{\"name\":\"x\",\"secretRefs\":{},\"secretValues\":{\"Password\":\"v\"}}")]
    public async Task A_patch_that_sends_secretRefs_is_a_400_naming_the_replacements_and_changes_nothing(string body)
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var id = await CreateConnectionAsync(client);
        var before = await StoredRefsAsync(id);
        var beforeSnapshot = await SecretSnapshotAsync(await ReferenceOfAsync(id));

        var response = await client.PatchAsync($"/api/connections/{id}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("clearSecretFields").And.Contain("secretValues");
        (await StoredRefsAsync(id)).Should().Be(before);
        (await SecretSnapshotAsync(await ReferenceOfAsync(id))).Should().Be(beforeSnapshot, "no version was added");
    }

    [Fact]
    public async Task A_principal_without_connection_update_cannot_clear_or_repair_a_field()
    {
        AssertSqlAvailable();
        using var owner = _fixture.CreateAuthenticatedClient(Operator);
        var id = await CreateConnectionAsync(owner);
        var before = await StoredRefsAsync(id);
        using var reader = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalConnectorCatalogReader);

        var clear = await reader.PatchAsJsonAsync($"/api/connections/{id}", new { clearSecretFields = new[] { "Password" } });
        var repair = await reader.PatchAsJsonAsync($"/api/connections/{id}", new { secretValues = new { Password = "x" } });

        clear.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        repair.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StoredRefsAsync(id)).Should().Be(before);
    }

    // ---- U-1 and U-3 together: refuse, then repair without knowing a reference ----

    [Fact]
    public async Task A_poisoned_field_is_refused_by_test_connect_and_repaired_by_a_value_without_the_foreign_secret_changing()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var victim = await CreateConnectionAsync(client, password: "victim-password-sentinel");
        var victimReference = await ReferenceOfAsync(victim);
        var poisoned = await CreateConnectionAsync(client);
        await SetStoredRefsAsync(poisoned, "{\"Password\":\"" + victimReference + "\"}");
        var victimBefore = await SecretSnapshotAsync(victimReference);

        // Test-connect: the existing masked failure, one audit row, no GUID anywhere, and the foreign secret untouched.
        var verify = await client.PostAsync($"/api/connections/{poisoned}/test-connect", null);

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await verify.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(verifyBody))
        {
            doc.RootElement.GetProperty("lastVerificationStatus").GetString().Should().Be("Failed");
            doc.RootElement.GetProperty("lastVerificationSummary").GetString().Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
        }

        verifyBody.Should().NotContain(victimReference).And.NotContain("victim-password-sentinel");
        var refusal = (await RefusalRowsAsync(poisoned)).Should().ContainSingle().Subject;
        refusal.Actor.Should().Be(ManagementGovernanceTestConstants.UserIntegrationAuthoring);
        refusal.Details.Should().NotContain(victimReference).And.NotContain(victim);
        using (var details = JsonDocument.Parse(refusal.Details))
        {
            details.RootElement.GetProperty("fieldKey").GetString().Should().Be("Password");
            details.RootElement.GetProperty("path").GetString().Should().Be("verify");
        }

        (await SecretSnapshotAsync(victimReference)).Should().Be(victimBefore);

        // Repair: only a value is sent. The field is repointed to a new secret the connection owns, atomically.
        var repair = await client.PatchAsJsonAsync($"/api/connections/{poisoned}", new { secretValues = new { Password = NewPasswordSentinel } });

        repair.StatusCode.Should().Be(HttpStatusCode.OK);
        var repairBody = await repair.Content.ReadAsStringAsync();
        repairBody.Should().NotContain(victimReference).And.NotContain(NewPasswordSentinel);
        var repairedReference = await ReferenceOfAsync(poisoned);
        repairedReference.Should().NotBe(victimReference);
        (await SecretSnapshotAsync(repairedReference)).Should().StartWith($"connection:{poisoned}:Password|platform-connection|");
        (await SecretSnapshotAsync(victimReference)).Should().Be(victimBefore, "the descriptor, versions and evidence of the foreign secret are exactly as they were");

        // And the refused field no longer refuses.
        var again = await client.PostAsync($"/api/connections/{poisoned}/test-connect", null);
        (await again.Content.ReadAsStringAsync()).Should().NotContain(ConnectionVerificationMessages.SecretResolutionFailed);
        (await RefusalRowsAsync(poisoned)).Should().ContainSingle("the repaired field resolves, so no further refusal row");
    }

    [Fact]
    public async Task An_undeclared_legacy_field_is_cleared_by_key_and_the_secret_it_named_is_left_alone()
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var victim = await CreateConnectionAsync(client);
        var victimReference = await ReferenceOfAsync(victim);
        var holder = await CreateConnectionAsync(client);
        var ownReference = await ReferenceOfAsync(holder);
        await SetStoredRefsAsync(holder, "{\"Password\":\"" + ownReference + "\",\"OldApiKey\":\"" + victimReference + "\"}");
        var victimBefore = await SecretSnapshotAsync(victimReference);

        var clear = await client.PatchAsJsonAsync($"/api/connections/{holder}", new { clearSecretFields = new[] { "OldApiKey" } });

        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await clear.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("secretFields").EnumerateArray().Select(f => f.GetProperty("key").GetString())
                .Should().BeEquivalentTo("Password");
        }

        (await StoredRefsAsync(holder)).Should().NotContain("OldApiKey").And.Contain(ownReference);
        (await SecretSnapshotAsync(victimReference)).Should().Be(victimBefore);
    }

    [Theory]
    [InlineData("{\"clearSecretFields\":[\"NoSuchField\"]}", "not a secret field of this connection")]
    [InlineData("{\"clearSecretFields\":[\"Server\"]}", "not a secret field")]
    [InlineData("{\"clearSecretFields\":[\" \"]}", "needs a key")]
    [InlineData("{\"clearSecretFields\":[\"Password\"],\"secretValues\":{\"Password\":\"v\"}}", "cannot be cleared and given a value")]
    public async Task A_malformed_clear_request_is_a_400_with_a_message_that_names_no_reference(string body, string expected)
    {
        AssertSqlAvailable();
        using var client = _fixture.CreateAuthenticatedClient(Operator);
        var id = await CreateConnectionAsync(client);
        var before = await StoredRefsAsync(id);

        var response = await client.PatchAsync($"/api/connections/{id}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain(expected).And.NotContain(await ReferenceOfAsync(id));
        (await StoredRefsAsync(id)).Should().Be(before);
    }
}
