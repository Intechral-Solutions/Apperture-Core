using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// End-to-end proof, through the REAL Management Host (real authorization, real audit store, real secret store, real SQL),
/// that the SQL structure browser is reachable only by a caller holding BOTH connection.read and connection.browse, browses
/// only structure, audits every remote request without content, reports the remote system's own refusal as a fixed outcome,
/// and never exposes a secret. Requires SQL Server: the fixture fails hard rather than skipping.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostConnectionToolsHttpTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string Suffix = Guid.NewGuid().ToString("N")[..8];

    private static readonly string Author = "svc-author-" + Suffix;
    private static readonly string Reader = "svc-reader-" + Suffix;
    private static readonly string Browser = "svc-browser-" + Suffix;
    private static readonly string BrowseOnly = "svc-browse-only-" + Suffix;

    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostConnectionToolsHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private static string Permissions(params string[] actions) =>
        "[" + string.Join(",", actions.Select(a => $"{{\"action\":\"{a}\",\"resource\":{{\"resourceType\":\"Connection\",\"scopeKey\":\"instance\"}}}}")) + "]";

    private async Task EnsurePrincipalAsync(string externalId, params string[] actions)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var roleId = "role-" + externalId;
        if (await db.Roles.AnyAsync(r => r.Id == roleId))
            return;

        var userId = "user-" + externalId;
        db.Roles.Add(new RoleEntity { Id = roleId, Name = "Connection tools test " + externalId, Description = "test", PermissionsJson = Permissions(actions), IsSystemRole = false });
        db.Users.Add(new UserEntity { Id = userId, DisplayName = externalId, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + externalId, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = externalId });
        db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateSqlConnectionAsync(HttpClient author, string password, string server, string database)
    {
        var builder = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var response = await author.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = WellKnownConnectorDefinitionIds.PlatformSql.Value,
            name = "Structure browse test " + Guid.NewGuid().ToString("N")[..6],
            config = new Dictionary<string, string>
            {
                ["Server"] = server,
                ["Database"] = database,
                ["UserId"] = builder.UserID,
                ["TrustServerCertificate"] = "true"
            },
            secretValues = new Dictionary<string, string> { ["Password"] = password }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
    }

    private async Task<(HttpClient Author, HttpClient Reader, HttpClient Browser, HttpClient BrowseOnly)> SetUpAsync()
    {
        _fixture.SqlAvailable.Should().BeTrue("the fixture fails hard: " + SqlServerTestConfiguration.DescribeActiveConfiguration());
        await EnsurePrincipalAsync(Author, "connection.create", "connection.read", "connection.update", "connection.delete");
        await EnsurePrincipalAsync(Reader, "connection.read", "connection.create", "connection.update", "connection.delete");
        await EnsurePrincipalAsync(Browser, "connection.read", "connection.browse");
        await EnsurePrincipalAsync(BrowseOnly, "connection.browse");
        return (_fixture.CreateAuthenticatedClient(Author), _fixture.CreateAuthenticatedClient(Reader),
            _fixture.CreateAuthenticatedClient(Browser), _fixture.CreateAuthenticatedClient(BrowseOnly));
    }

    private async Task<int> StructureAuditCountAsync(string connectionId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.AuditLogs.CountAsync(a => a.Action == "connection.structure_browsed" && a.Details!.Contains(connectionId));
    }

    private static string Password(string connectionString) => new SqlConnectionStringBuilder(connectionString).Password;

    private static Task<HttpResponseMessage> BrowseAsync(HttpClient client, string connectionId, string operation, object? body = null) =>
        client.PostAsJsonAsync($"/api/connections/{connectionId}/tools/sql-structure/{operation}", body ?? new { });

    // ---- Request forgery: remote browse is POST-only with a JSON body -----------------------------------

    [Fact]
    public async Task RF_a_GET_can_no_longer_perform_a_remote_browse_and_writes_nothing()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);

        foreach (var path in new[] { "schemas", "tables?schema=dbo", "columns?schema=dbo&table=x" })
        {
            // Even a fully authorized caller, and even with the values a working POST would carry.
            var response = await browser.GetAsync($"/api/connections/{id}/tools/sql-structure/{path}");
            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, $"GET {path}");
        }

        (await StructureAuditCountAsync(id)).Should().Be(0, "a refused GET must not reach the provider or the audit");
    }

    [Fact]
    public async Task RF_a_POST_that_is_not_a_JSON_body_is_refused_before_any_remote_io_or_audit()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);
        var url = $"/api/connections/{id}/tools/sql-structure/tables";

        // The requests a cross-site HTML form (or a query-string-only POST) can produce.
        var formEncoded = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["schema"] = "dbo" }) };
        var textPlain = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{\"schema\":\"dbo\"}", System.Text.Encoding.UTF8, "text/plain") };
        var noBody = new HttpRequestMessage(HttpMethod.Post, url + "?schema=dbo");
        var emptyJson = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("", System.Text.Encoding.UTF8, "application/json") };

        foreach (var (label, request) in new[] { ("form", formEncoded), ("text/plain", textPlain), ("no body", noBody), ("empty json", emptyJson) })
        {
            var response = await browser.SendAsync(request);
            ((int)response.StatusCode).Should().BeOneOf([400, 415], $"{label} must be refused by the framework before the action runs");
        }

        (await StructureAuditCountAsync(id)).Should().Be(0, "none of these may reach the provider or write a browse audit");
    }

    [Fact]
    public async Task RF_an_unauthenticated_POST_is_refused_and_a_legitimate_POST_still_succeeds()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);

        var anonymous = _fixture.Factory!.CreateClient();
        var refused = await BrowseAsync(anonymous, id, "schemas");
        var allowed = await BrowseAsync(browser, id, "schemas");

        ((int)refused.StatusCode).Should().BeOneOf([401, 403]);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StructureAuditCountAsync(id)).Should().Be(1, "exactly the one legitimate request is audited");
    }

    // ---- Authorization, end to end ---------------------------------------------------------------------

    [Fact]
    public async Task AZ_only_a_caller_holding_both_grants_can_use_the_tools_and_every_refusal_is_a_plain_403()
    {
        var (author, reader, browser, browseOnly) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);

        // read (plus create/update/delete) is not enough; browse alone is not enough; the response body says nothing either way.
        foreach (var client in new[] { reader, browseOnly, author })
        {
            var responses = new List<(string Path, HttpResponseMessage Response)>
            {
                ("tools", await client.GetAsync($"/api/connections/{id}/tools")),
                ("schemas", await BrowseAsync(client, id, "schemas")),
                ("tables", await BrowseAsync(client, id, "tables", new { schema = "dbo" })),
                ("columns", await BrowseAsync(client, id, "columns", new { schema = "dbo", table = "x" })),
            };
            foreach (var (path, response) in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{path} must refuse a caller without both grants");
                (await response.Content.ReadAsStringAsync()).Should().NotContain("sqlStructureBrowser");
            }
        }

        var tools = await browser.GetAsync($"/api/connections/{id}/tools");
        tools.StatusCode.Should().Be(HttpStatusCode.OK);
        (await tools.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("sqlStructureBrowser").GetBoolean().Should().BeTrue();

        (await StructureAuditCountAsync(id)).Should().Be(0, "refused calls and the availability lookup write no browse audit");
    }

    [Fact]
    public async Task The_capability_flag_is_true_only_for_a_caller_who_can_read_and_browse()
    {
        var (_, reader, browser, browseOnly) = await SetUpAsync();

        async Task<bool> CanBrowse(HttpClient c) =>
            (await c.GetFromJsonAsync<JsonElement>("/api/control/capabilities", Json)).GetProperty("canBrowseConnections").GetBoolean();

        (await CanBrowse(browser)).Should().BeTrue();
        (await CanBrowse(reader)).Should().BeFalse();
        (await CanBrowse(browseOnly)).Should().BeFalse();
    }

    // ---- The real browse, end to end -------------------------------------------------------------------

    [Fact]
    public async Task A_browser_reads_real_structure_audits_each_request_without_content_and_exposes_no_secret()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var password = Password(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, password, target.DataSource, target.InitialCatalog);
        var bodies = new List<string>();

        var schemasResponse = await BrowseAsync(browser, id, "schemas");
        bodies.Add(await schemasResponse.Content.ReadAsStringAsync());
        var schemas = JsonSerializer.Deserialize<JsonElement>(bodies[^1], Json);
        var tablesResponse = await BrowseAsync(browser, id, "tables", new { schema = "dbo" });
        bodies.Add(await tablesResponse.Content.ReadAsStringAsync());
        var tables = JsonSerializer.Deserialize<JsonElement>(bodies[^1], Json);
        var firstTable = tables.GetProperty("items").EnumerateArray().First(t => !t.GetProperty("isView").GetBoolean()).GetProperty("name").GetString()!;
        var columnsResponse = await BrowseAsync(browser, id, "columns", new { schema = "dbo", table = firstTable });
        bodies.Add(await columnsResponse.Content.ReadAsStringAsync());
        var columns = JsonSerializer.Deserialize<JsonElement>(bodies[^1], Json);

        schemasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        schemas.GetProperty("outcome").GetString().Should().Be("Succeeded");
        schemas.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Contain("dbo");
        schemas.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().NotContain(["sys", "INFORMATION_SCHEMA"]);
        tables.GetProperty("outcome").GetString().Should().Be("Succeeded");
        tables.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        columns.GetProperty("outcome").GetString().Should().Be("Succeeded");
        columns.GetProperty("items").EnumerateArray()
            .All(c => c.TryGetProperty("dataType", out var _1) && c.TryGetProperty("isNullable", out var _2)).Should().BeTrue();
        columns.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);

        // Structure only: the payload has no row/value-shaped property anywhere, and no secret or connection string.
        foreach (var body in bodies)
        {
            body.Should().NotContain(password).And.NotContainEquivalentOf("Password=").And.NotContainEquivalentOf("Data Source");
            body.Should().NotContain("\"rows\"").And.NotContain("\"values\"").And.NotContain("\"data\"");
        }

        // Exactly one audit record per request, valid JSON, no content.
        (await StructureAuditCountAsync(id)).Should().Be(3);
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var records = await db.AuditLogs.Where(a => a.Action == "connection.structure_browsed" && a.Details!.Contains(id)).ToListAsync();
        records.Select(r => JsonDocument.Parse(r.Details!).RootElement.GetProperty("operation").GetString())
            .Should().BeEquivalentTo("schemas", "tables", "columns");
        records.Should().OnlyContain(r => r.Outcome == "succeeded" && r.ResourceContextId == "shared");
        foreach (var record in records)
        {
            record.Details.Should().NotContain(firstTable).And.NotContain("\"dbo\"").And.NotContain(password);
            JsonDocument.Parse(record.Details!).RootElement.GetProperty("toolOutcome").GetString().Should().Be("Succeeded");
        }
    }

    [Fact]
    public async Task An_injection_shaped_schema_is_an_inert_value_and_changes_nothing()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var before = await db.Connections.CountAsync();

        var hostile = ("dbo'; DELETE FROM Connections; DROP TABLE Connections;--");
        var response = await BrowseAsync(browser, id, "tables", new { schema = hostile });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("outcome").GetString().Should().Be("Succeeded");
        body.GetProperty("items").GetArrayLength().Should().Be(0, "the hostile text matched no schema; it was only ever a value");
        (await db.Connections.AsNoTracking().CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_bad_request_is_a_400_and_writes_no_audit()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);

        var empty = await BrowseAsync(browser, id, "tables");
        var tooLong = await BrowseAsync(browser, id, "tables", new { schema = new string((char)120, 200) });

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StructureAuditCountAsync(id)).Should().Be(0);
    }

    // ---- The remote system's own answer ----------------------------------------------------------------

    [Fact]
    public async Task A_login_the_database_refuses_is_a_200_with_a_fixed_denied_outcome_and_a_failed_audit_record()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, "definitely-wrong-" + Guid.NewGuid().ToString("N"), target.DataSource, target.InitialCatalog);

        var response = await BrowseAsync(browser, id, "schemas");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal by the remote system is its answer, not a platform 500");
        var json = JsonSerializer.Deserialize<JsonElement>(body, Json);
        json.GetProperty("outcome").GetString().Should().Be("RemoteDenied");
        json.GetProperty("message").GetString().Should().Be(
            "The database refused this request for the connection's login. What structure is visible depends on that login's permissions.");
        body.Should().NotContainEquivalentOf("Login failed");
        json.GetProperty("items").GetArrayLength().Should().Be(0);

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<AppertureDbContext>().AuditLogs
            .SingleAsync(a => a.Action == "connection.structure_browsed" && a.Details!.Contains(id));
        record.Outcome.Should().Be("failed");
        record.Details.Should().Contain("RemoteDenied").And.NotContainEquivalentOf("Login failed");
    }

    [Fact]
    public async Task An_unreachable_server_is_a_200_with_a_fixed_safe_outcome_and_no_endpoint_details()
    {
        // 127.0.0.1,1 is a closed local port, not a cross-platform transport oracle. Windows SqlClient
        // reports 1225 and the classifier returns RemoteUnavailable. Ubuntu reports SqlException Number=0
        // with SocketError.Success, which stays Failed. Both answers are fixed sentences and must not
        // leak the endpoint. Known transport codes are covered by PlatformSqlStructureBrowserTests.
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), "127.0.0.1,1", target.InitialCatalog);

        var response = await BrowseAsync(browser, id, "schemas");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = json.GetProperty("outcome").GetString();
        var message = json.GetProperty("message").GetString();
        outcome.Should().BeOneOf("RemoteUnavailable", "Failed");
        message.Should().Be(outcome == "RemoteUnavailable"
            ? "The database could not be reached, or did not answer in time."
            : "The structure request could not be completed.");
        json.GetProperty("items").GetArrayLength().Should().Be(0);
        json.GetRawText().Should().NotContain("127.0.0.1");
    }

    // ---- Lifecycle and composition ---------------------------------------------------------------------

    [Fact]
    public async Task A_retired_connection_offers_no_tools_and_refuses_structure_requests()
    {
        var (author, _, browser, _) = await SetUpAsync();
        var target = new SqlConnectionStringBuilder(_fixture.ConnectionString!);
        var id = await CreateSqlConnectionAsync(author, Password(_fixture.ConnectionString!), target.DataSource, target.InitialCatalog);
        (await author.DeleteAsync($"/api/connections/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tools = await browser.GetFromJsonAsync<JsonElement>($"/api/connections/{id}/tools", Json);
        var schemas = await BrowseAsync(browser, id, "schemas");

        tools.GetProperty("sqlStructureBrowser").GetBoolean().Should().BeFalse();
        schemas.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StructureAuditCountAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_connection_is_a_404()
    {
        var (_, _, browser, _) = await SetUpAsync();

        (await browser.GetAsync("/api/connections/does-not-exist/tools")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BrowseAsync(browser, "does-not-exist", "schemas")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PR1_PR2_PR3_the_real_host_composes_exactly_one_provider_and_it_is_platform_sql()
    {
        await SetUpAsync();
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();

        var providers = scope.ServiceProvider.GetServices<IConnectionToolProvider>().ToList();

        providers.Should().ContainSingle();
        providers[0].ConnectorDefinitionId.Should().Be(WellKnownConnectorDefinitionIds.PlatformSql);
        providers[0].Should().BeAssignableTo<ISqlStructureBrowser>();
        providers.Select(p => p.ConnectorDefinitionId).Should().NotContain(WellKnownConnectorDefinitionIds.PlatformSftp)
            .And.NotContain(WellKnownConnectorDefinitionIds.PlatformCampusNexus);
    }
}
