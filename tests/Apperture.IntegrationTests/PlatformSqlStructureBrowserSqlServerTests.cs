using Apperture.ConnectorResolution;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// The SQL structure browser against a REAL, throwaway, migrated SQL Server catalog (the fixture fails hard rather than
/// skipping). It proves the metadata shape (schemas, tables, views, columns), the item cap on real data, cancellation, and
/// that nothing but structure is returned. The only DDL is one view created in the scratch catalog by the test's own setup;
/// the browser itself never issues anything but its closed set of read-only metadata statements.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PlatformSqlStructureBrowserSqlServerTests
{
    private sealed class FixedResolver(string password) : ISecretResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResolvedSecret(secretRef, new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: password)));
    }

    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string prefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(prefix);
        fixture.Should().NotBeNull(
            "the structure browser's closure requires this suite to genuinely execute against SQL Server. "
            + SqlServerTestConfiguration.DescribeActiveConfiguration());
        return fixture!;
    }

    private static (PlatformSqlStructureBrowser Browser, Connection Connection) Build(
        Apperture.Persistence.SqlServer.AppertureDbContext db, SqlStructureBrowserOptions? options = null)
    {
        var target = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        var connection = new Connection(
            new ConnectionId("scratch"), WellKnownConnectorDefinitionIds.PlatformSql, "Scratch",
            new Dictionary<string, string>
            {
                ["Server"] = target.DataSource, ["Database"] = target.InitialCatalog,
                ["UserId"] = target.UserID, ["TrustServerCertificate"] = "true"
            },
            new Dictionary<string, string> { ["Password"] = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11" }, null, null);
        return (new PlatformSqlStructureBrowser(new FixedResolver(target.Password), new PlatformSqlConnectorClientFactory(), options), connection);
    }

    [Fact]
    public async Task Schemas_tables_views_and_columns_have_the_expected_structure_only_shape()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_StructBrowse1_");
        await using var db = fixture.NewDb();
        await db.Database.ExecuteSqlRawAsync("CREATE VIEW dbo.StructureBrowseProofView AS SELECT CAST(1 AS int) AS Marker");
        var (browser, connection) = Build(db);
        var table = db.Model.FindEntityType(typeof(Apperture.Persistence.SqlServer.ConnectionEntity))!.GetTableName()!;

        var schemas = await browser.ListSchemasAsync(connection, CancellationToken.None);
        var tables = await browser.ListTablesAsync(connection, "dbo", CancellationToken.None);
        var columns = await browser.ListColumnsAsync(connection, "dbo", table, CancellationToken.None);
        var viewColumns = await browser.ListColumnsAsync(connection, "dbo", "StructureBrowseProofView", CancellationToken.None);

        schemas.Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
        schemas.Items.Select(s => s.Name).Should().Contain("dbo").And.NotContain(["sys", "INFORMATION_SCHEMA", "db_owner"]);
        tables.Items.Should().Contain(t => t.Name == table && !t.IsView && t.Schema == "dbo");
        tables.Items.Should().Contain(t => t.Name == "StructureBrowseProofView" && t.IsView);
        // Ordered by the database's own collation (case-insensitive), so the test asserts stability and uniqueness, not ordinal order.
        tables.Items.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        columns.Items.Should().Contain(c => c.Name == "Id");
        columns.Items.Select(c => c.Ordinal).Should().BeInAscendingOrder();
        columns.Items.Should().OnlyContain(c => !string.IsNullOrEmpty(c.DataType));
        columns.Items.Should().Contain(c => c.DataType == "nvarchar" && c.MaxLength > 0, "declared lengths are reported");
        var marker = viewColumns.Items.Should().ContainSingle().Subject;
        (marker.Name, marker.DataType, marker.Ordinal, marker.MaxLength).Should().Be(("Marker", "int", 1, (int?)null));
        tables.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_schema_or_table_is_an_empty_successful_result_not_an_error()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_StructBrowse2_");
        await using var db = fixture.NewDb();
        var (browser, connection) = Build(db);

        var tables = await browser.ListTablesAsync(connection, "no_such_schema", CancellationToken.None);
        var columns = await browser.ListColumnsAsync(connection, "dbo", "no_such_table", CancellationToken.None);

        tables.Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
        tables.Items.Should().BeEmpty();
        columns.Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
        columns.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task The_cap_is_enforced_on_real_data_and_truncation_is_reported()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_StructBrowse3_");
        await using var db = fixture.NewDb();
        var (capped, connection) = Build(db, new SqlStructureBrowserOptions { MaxItems = 3 });
        var (uncapped, _) = Build(db);

        var all = await uncapped.ListTablesAsync(connection, "dbo", CancellationToken.None);
        var first = await capped.ListTablesAsync(connection, "dbo", CancellationToken.None);

        all.Items.Count.Should().BeGreaterThan(3, "the migrated catalog has more than three tables");
        first.Items.Should().HaveCount(3);
        first.Truncated.Should().BeTrue();
        first.Limit.Should().Be(3);
        first.Items.Should().Equal(all.Items.Take(3), "the first N in a stable order");
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_not_reported_as_a_failure()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_StructBrowse4_");
        await using var db = fixture.NewDb();
        var (browser, connection) = Build(db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await browser.ListSchemasAsync(connection, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task M6_cancelling_in_the_middle_of_opening_a_real_connection_stops_promptly_with_a_cancellation()
    {
        // A local "server" that accepts the TCP connection and never answers: the login handshake hangs, deterministically,
        // until the caller cancels. No SQL Server is involved, and nothing here depends on the network.
        using var blackhole = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        blackhole.Start();
        var accepted = blackhole.AcceptTcpClientAsync();
        var port = ((System.Net.IPEndPoint)blackhole.LocalEndpoint).Port;
        var connection = new Connection(
            new ConnectionId("blackhole"), WellKnownConnectorDefinitionIds.PlatformSql, "Blackhole",
            new Dictionary<string, string> { ["Server"] = $"127.0.0.1,{port}", ["Database"] = "x", ["UserId"] = "u", ["TrustServerCertificate"] = "true" },
            new Dictionary<string, string> { ["Password"] = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11" }, null, null);
        var browser = new PlatformSqlStructureBrowser(
            new FixedResolver("p"), new PlatformSqlConnectorClientFactory(), new SqlStructureBrowserOptions { ConnectTimeoutSeconds = 60 });
        using var cts = new CancellationTokenSource();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var pending = browser.ListSchemasAsync(connection, cts.Token).AsTask();
        await accepted.WaitAsync(TimeSpan.FromSeconds(10));
        cts.CancelAfter(200);
        var act = async () => await pending;

        await act.Should().ThrowAsync<OperationCanceledException>();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "cancellation, not the 60 second connect timeout, ended it");
    }

    [Fact]
    public async Task A_database_the_login_cannot_open_is_the_remote_systems_refusal()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_StructBrowse5_");
        await using var db = fixture.NewDb();
        var (browser, connection) = Build(db);
        var missingDatabase = connection with
        {
            Config = new Dictionary<string, string>(connection.Config) { ["Database"] = "Apperture_no_such_db_" + Guid.NewGuid().ToString("N") }
        };

        var result = await browser.ListSchemasAsync(missingDatabase, CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.RemoteDenied);
        result.Message.Should().StartWith("The database refused this request");
        result.Items.Should().BeEmpty();
    }
}
