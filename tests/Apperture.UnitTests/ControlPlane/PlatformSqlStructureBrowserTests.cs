using System.Data;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// SF1-SF9: the SQL structure browser sends only its closed set of fixed, parameterized metadata statements, binds every
/// selection as a parameter VALUE, bounds every request (timeout, cap, cancellation), reports failures as fixed safe outcomes,
/// and always disposes what it creates.
/// </summary>
public sealed class PlatformSqlStructureBrowserTests
{
    private const string LeakyMessage = "Secret 3f2a9c1e (vault://prod/kv/sql-password) not found for key Password";

    private static Connection SqlConnectionRow(bool withSecret = false) =>
        new(
            new ConnectionId("conn-1"), WellKnownConnectorDefinitionIds.PlatformSql, "Warehouse",
            new Dictionary<string, string> { ["Server"] = "db.example", ["Database"] = "Ops", ["UserId"] = "reader" },
            withSecret ? new Dictionary<string, string> { ["Password"] = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11" } : new Dictionary<string, string>(),
            null, null);

    private sealed class Rig
    {
        public required PlatformSqlStructureBrowser Sut { get; init; }
        public required RecordingDbConnection Db { get; init; }
        public required Mock<IConnectorClientFactory<IDbConnection>> Factory { get; init; }
        public required Mock<ISecretResolver> Secrets { get; init; }
    }

    private static Rig NewRig(SqlStructureBrowserOptions? options = null, Action<RecordingDbConnection>? configure = null)
    {
        var db = new RecordingDbConnection();
        configure?.Invoke(db);
        var factory = new Mock<IConnectorClientFactory<IDbConnection>>();
        factory.Setup(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>())).Returns(db);
        var secrets = new Mock<ISecretResolver>();
        secrets.Setup(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef("x"), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "p@ss")));
        return new Rig { Sut = new PlatformSqlStructureBrowser(secrets.Object, factory.Object, options), Db = db, Factory = factory, Secrets = secrets };
    }

    private static DataTable Table(params string[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
            table.Columns.Add(column, typeof(object));
        return table;
    }

    private static DataTable Schemas(int count)
    {
        var table = Table("SCHEMA_NAME");
        for (var i = 0; i < count; i++)
            table.Rows.Add($"schema{i:000}");
        return table;
    }

    // SF2 -------------------------------------------------------------------------------------------

    [Fact]
    public void SF2_the_statement_set_is_exactly_these_three_literals()
    {
        SqlStructureQueries.All.Should().Equal(
            "SELECT TOP (@limit) [SCHEMA_NAME] FROM INFORMATION_SCHEMA.SCHEMATA WHERE [SCHEMA_NAME] NOT IN (N'sys', N'INFORMATION_SCHEMA', N'guest', N'db_owner', N'db_accessadmin', N'db_securityadmin', N'db_ddladmin', N'db_backupoperator', N'db_datareader', N'db_datawriter', N'db_denydatareader', N'db_denydatawriter') ORDER BY [SCHEMA_NAME]",
            "SELECT TOP (@limit) TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema ORDER BY TABLE_NAME",
            "SELECT TOP (@limit) COLUMN_NAME, DATA_TYPE, IS_NULLABLE, ORDINAL_POSITION, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION");
    }

    [Fact]
    public void SF2_SF7_no_statement_can_change_anything_batch_execute_or_read_rows()
    {
        foreach (var statement in SqlStructureQueries.All)
        {
            statement.Should().StartWith("SELECT TOP (@limit) ");
            statement.Should().NotContain(";", "one statement, no batch");
            statement.Should().NotMatchRegex(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|DENY|EXEC|EXECUTE|sp_executesql|QUOTENAME|OPENROWSET|OPENQUERY|OPENDATASOURCE|BULK|INTO|WAITFOR|GO)\b");
            statement.Should().NotContain("*");
            Regex.Matches(statement, @"(?i)\bFROM\s+(\S+)").Select(m => m.Groups[1].Value)
                .Should().OnlyContain(source => source.StartsWith("INFORMATION_SCHEMA.", StringComparison.Ordinal),
                    "only metadata views are ever read, never a user table");
        }
    }

    [Fact]
    public async Task SF2_every_command_actually_sent_belongs_to_the_closed_set()
    {
        var rig = NewRig(configure: db => db.Rows = c => c.CommandText.Contains("COLUMNS") ? Table("a", "b", "c", "d", "e") : Table("a", "b", "c"));

        await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);
        await rig.Sut.ListTablesAsync(SqlConnectionRow(), "dbo", CancellationToken.None);
        await rig.Sut.ListColumnsAsync(SqlConnectionRow(), "dbo", "Orders", CancellationToken.None);

        rig.Db.Commands.Should().HaveCount(3);
        rig.Db.Commands.Select(c => c.CommandText).Should().OnlyContain(text => SqlStructureQueries.All.Contains(text));
    }

    // SF1 / SF3 -------------------------------------------------------------------------------------

    [Fact]
    public async Task SF1_every_selection_is_bound_as_a_typed_parameter_value()
    {
        var rig = NewRig(configure: db => db.Rows = _ => Table("a", "b", "c", "d", "e"));

        await rig.Sut.ListColumnsAsync(SqlConnectionRow(), "sales", "Orders", CancellationToken.None);

        var parameters = rig.Db.Commands.Single().BoundParameters.ToDictionary(p => p.ParameterName);
        parameters.Keys.Should().BeEquivalentTo("@limit", "@schema", "@table");
        parameters["@schema"].Value.Should().Be("sales");
        parameters["@table"].Value.Should().Be("Orders");
        parameters["@schema"].DbType.Should().Be(DbType.String);
        parameters["@schema"].Size.Should().Be(128);
        parameters["@limit"].DbType.Should().Be(DbType.Int32);
        parameters["@limit"].Value.Should().Be(501);
    }

    [Theory]
    [InlineData("dbo'; DROP TABLE Connections;--")]
    [InlineData("dbo]; EXEC xp_cmdshell 'dir'; --")]
    [InlineData("' OR 1=1 --")]
    [InlineData("[dbo]")]
    [InlineData("dbo\r\nGO\r\nDELETE FROM Users")]
    public async Task SF3_an_injection_shaped_name_is_an_inert_parameter_value_and_never_reaches_command_text(string hostile)
    {
        var rig = NewRig();

        var tables = await rig.Sut.ListTablesAsync(SqlConnectionRow(), hostile, CancellationToken.None);
        var columns = await rig.Sut.ListColumnsAsync(SqlConnectionRow(), hostile, hostile, CancellationToken.None);

        tables.Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
        tables.Items.Should().BeEmpty();
        columns.Items.Should().BeEmpty();
        foreach (var command in rig.Db.Commands)
        {
            SqlStructureQueries.All.Should().Contain(command.CommandText, "the text is one of the fixed literals, byte for byte");
            command.CommandText.Should().NotContain(hostile.Split('\r')[0], "no operator value is ever part of the SQL text");
            command.BoundParameters.Where(p => p.ParameterName != "@limit").Should().OnlyContain(p => (string?)p.Value == hostile);
        }
    }

    [Fact]
    public async Task SF3_a_name_over_the_identifier_limit_or_an_empty_name_is_rejected_before_any_client_exists()
    {
        var rig = NewRig();

        var tooLong = async () => await rig.Sut.ListTablesAsync(SqlConnectionRow(), new string('x', 129), CancellationToken.None);
        var empty = async () => await rig.Sut.ListColumnsAsync(SqlConnectionRow(), "dbo", " ", CancellationToken.None);

        await tooLong.Should().ThrowAsync<ArgumentException>();
        await empty.Should().ThrowAsync<ArgumentException>();
        rig.Factory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Never);
        rig.Secrets.Verify(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // SF4 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SF4_every_command_has_an_explicit_timeout_by_default_and_when_configured()
    {
        var rigDefault = NewRig();
        await rigDefault.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);
        var rigTight = NewRig(new SqlStructureBrowserOptions { CommandTimeoutSeconds = 3 });
        await rigTight.Sut.ListTablesAsync(SqlConnectionRow(), "dbo", CancellationToken.None);

        rigDefault.Db.Commands.Single().CommandTimeout.Should().Be(15, "an explicit value, never the provider default of 30 or 0 (unbounded)");
        rigTight.Db.Commands.Single().CommandTimeout.Should().Be(3);
        rigDefault.Db.Commands.Single().CommandType.Should().Be(CommandType.Text);
    }

    // SF5 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SF5_the_callers_token_reaches_the_execute_call_and_cancellation_propagates_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var rig = NewRig(configure: db => db.OnExecute = async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var pending = rig.Sut.ListSchemasAsync(SqlConnectionRow(), cts.Token).AsTask();
        await started.Task;
        cts.Cancel();

        await pending.Invoking(p => p).Should().ThrowAsync<OperationCanceledException>();
        rig.Db.Commands.Single().TokenSeen.Should().Be(cts.Token);
        rig.Db.Disposed.Should().BeTrue();
        rig.Db.Commands.Single().Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SF5_an_already_cancelled_request_opens_nothing_and_is_not_reported_as_a_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var rig = NewRig();

        var act = async () => await rig.Sut.ListSchemasAsync(SqlConnectionRow(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.Db.Commands.Should().BeEmpty();
        rig.Db.Disposed.Should().BeTrue();
    }

    // SF6 -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4, 3, 3, false)]
    [InlineData(4, 4, 4, false)]
    [InlineData(4, 5, 4, true)]
    [InlineData(4, 100, 4, true)]
    [InlineData(4, 0, 0, false)]
    public async Task SF6_the_cap_is_enforced_and_truncation_is_reported_honestly(int cap, int available, int expectedItems, bool expectedTruncated)
    {
        var rig = NewRig(new SqlStructureBrowserOptions { MaxItems = cap }, db => db.Rows = _ => Schemas(available));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
        result.Items.Should().HaveCount(expectedItems);
        result.Truncated.Should().Be(expectedTruncated);
        result.Limit.Should().Be(cap);
        rig.Db.Commands.Single().BoundParameters.Single(p => p.ParameterName == "@limit").Value.Should().Be(cap + 1,
            "one extra row is requested only to learn whether the list was cut");
    }

    [Fact]
    public async Task The_default_cap_is_five_hundred()
    {
        new SqlStructureBrowserOptions().MaxItems.Should().Be(500);
        var rig = NewRig();
        (await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None)).Limit.Should().Be(500);
    }

    // Row mapping: structure only -------------------------------------------------------------------

    [Fact]
    public async Task Tables_views_and_columns_map_to_structure_only_shapes()
    {
        var rig = NewRig(configure: db => db.Rows = c =>
        {
            if (c.CommandText.Contains("TABLES"))
            {
                var t = Table("TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE");
                t.Rows.Add("dbo", "Orders", "BASE TABLE");
                t.Rows.Add("dbo", "OrderSummary", "VIEW");
                return t;
            }

            var cols = Table("COLUMN_NAME", "DATA_TYPE", "IS_NULLABLE", "ORDINAL_POSITION", "CHARACTER_MAXIMUM_LENGTH");
            cols.Rows.Add("Id", "int", "NO", 1, DBNull.Value);
            cols.Rows.Add("Name", "nvarchar", "YES", 2, 200);
            return cols;
        });

        var tables = await rig.Sut.ListTablesAsync(SqlConnectionRow(), "dbo", CancellationToken.None);
        var columns = await rig.Sut.ListColumnsAsync(SqlConnectionRow(), "dbo", "Orders", CancellationToken.None);

        tables.Items.Should().Equal(new SqlTableInfo("dbo", "Orders", false), new SqlTableInfo("dbo", "OrderSummary", true));
        columns.Items.Should().Equal(new SqlColumnInfo("Id", "int", false, 1, null), new SqlColumnInfo("Name", "nvarchar", true, 2, 200));
    }

    // SF7 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SF7_only_a_single_result_reader_is_ever_executed_and_never_a_scalar_or_non_query()
    {
        var rig = NewRig();

        await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        var command = rig.Db.Commands.Single();
        command.Executed.Should().BeTrue();
        command.Behavior.Should().Be(CommandBehavior.SingleResult);
        // ExecuteScalar and ExecuteNonQuery THROW in the recording double, so reaching this line proves neither was called.
    }

    // SF8 -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(18456, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(4060, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(916, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(229, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(262, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(297, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(300, ConnectionToolOutcome.RemoteDenied)]
    [InlineData(-2, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(53, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(40, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(258, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(1225, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(1232, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(10060, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(10061, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(11001, ConnectionToolOutcome.RemoteUnavailable)]
    [InlineData(111, ConnectionToolOutcome.Failed)]
    [InlineData(110, ConnectionToolOutcome.Failed)]
    [InlineData(104, ConnectionToolOutcome.Failed)]
    [InlineData(101, ConnectionToolOutcome.Failed)]
    [InlineData(113, ConnectionToolOutcome.Failed)]
    [InlineData(208, ConnectionToolOutcome.Failed)]
    [InlineData(0, ConnectionToolOutcome.Failed)]
    public void SF8_sql_error_numbers_map_to_the_remote_systems_own_answer(int number, ConnectionToolOutcome expected) =>
        PlatformSqlStructureBrowser.ClassifySqlNumber(number).Should().Be(expected);

    [Fact]
    public async Task SF8_a_timeout_is_reported_as_the_remote_not_answering_not_as_a_platform_error()
    {
        var rig = NewRig(configure: db => db.OpenFailure = new TimeoutException("Execution Timeout Expired for db.example port 1433"));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.RemoteUnavailable);
        result.Items.Should().BeEmpty();
        result.Message.Should().Be("The database could not be reached, or did not answer in time.");
        result.Message.Should().NotContain("db.example");
    }

    [Fact]
    public async Task SF8_a_connection_refused_socket_is_the_remote_system_being_unreachable()
    {
        var rig = NewRig(configure: db => db.OpenFailure = new SocketException((int)SocketError.ConnectionRefused));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.RemoteUnavailable);
        result.Message.Should().Be("The database could not be reached, or did not answer in time.");
    }

    [Fact]
    public async Task SF8_an_unclassified_wrapper_still_follows_an_inner_connection_refused_socket()
    {
        var rig = NewRig(configure: db => db.OpenFailure = new InvalidOperationException(
            "Data Source=db.example;Password=hunter2",
            new SocketException((int)SocketError.ConnectionRefused)));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.RemoteUnavailable);
        result.Message.Should().Be("The database could not be reached, or did not answer in time.");
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("hunter2").And.NotContain("db.example");
    }

    [Fact]
    public async Task SF8_a_socket_access_denied_stays_a_generic_failure()
    {
        var rig = NewRig(configure: db => db.OpenFailure = new SocketException((int)SocketError.AccessDenied));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        result.Message.Should().Be("The structure request could not be completed.");
    }

    [Fact]
    public async Task SF8_a_socket_success_code_stays_a_generic_failure()
    {
        // The Ubuntu closed-port capture was SqlException Number=0 around SocketError.Success.
        // That inner code is not a refused, timed-out, or unreachable transport signal.
        var rig = NewRig(configure: db => db.OpenFailure = new SocketException((int)SocketError.Success));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        result.Message.Should().Be("The structure request could not be completed.");
    }

    [Fact]
    public async Task SF8_an_unrecognised_failure_is_a_generic_fixed_message_and_never_the_exception_text()
    {
        var rig = NewRig(configure: db => db.OpenFailure = new InvalidOperationException("Data Source=db.example;Password=hunter2"));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        result.Message.Should().Be("The structure request could not be completed.");
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("hunter2").And.NotContain("db.example");
    }

    [Fact]
    public async Task Unresolvable_secrets_are_a_fixed_safe_outcome_and_no_client_is_created()
    {
        var rig = NewRig();
        rig.Secrets.Setup(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(LeakyMessage));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(withSecret: true), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.SecretsUnavailable);
        result.Message.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("vault://").And.NotContain("3f2a9c1e");
        rig.Factory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Never);
    }

    [Fact]
    public async Task Incomplete_stored_settings_are_reported_without_the_factory_message()
    {
        var rig = NewRig();
        rig.Factory.Setup(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()))
            .Throws(new InvalidOperationException("Platform SQL connection requires 'Server' in config or resolved material."));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        result.Message.Should().Be("The connection's stored settings are incomplete.");
    }

    [Fact]
    public async Task A_client_that_is_not_a_database_connection_is_disposed_and_reported_as_a_failure()
    {
        var rig = NewRig();
        var notDb = new Mock<IDbConnection>();
        rig.Factory.Setup(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>())).Returns(notDb.Object);

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        notDb.Verify(c => c.Dispose(), Times.Once);
    }

    // SF9 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SF9_success_disposes_the_connection_the_command_and_the_reader()
    {
        var rig = NewRig();

        await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        rig.Db.Disposed.Should().BeTrue();
        rig.Db.Commands.Single().Disposed.Should().BeTrue();
        rig.Db.Commands.Single().Reader!.IsClosed.Should().BeTrue();
        rig.Db.OpenCount.Should().Be(1, "one client, one open, per request");
    }

    [Fact]
    public async Task SF9_a_failure_after_the_client_exists_still_disposes_it()
    {
        var rig = NewRig(configure: db => db.OnExecute = (_, _) => throw new InvalidOperationException("boom"));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        rig.Db.Disposed.Should().BeTrue();
        rig.Db.Commands.Single().Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SF9_each_request_gets_its_own_client_and_nothing_is_cached()
    {
        var rig = NewRig();

        await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);
        await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None);

        rig.Factory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Exactly(2));
    }

    // I1: the remote LOGIN is the boundary ----------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task I1_a_connection_with_no_sql_login_is_refused_before_any_client_exists(string? userId)
    {
        var rig = NewRig();
        var config = new Dictionary<string, string> { ["Server"] = "db.example", ["Database"] = "Ops" };
        if (userId is not null)
            config["UserId"] = userId;
        var integratedSecurity = SqlConnectionRow() with { Config = config };

        var result = await rig.Sut.ListSchemasAsync(integratedSecurity, CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.Failed);
        result.Message.Should().Contain("own SQL login").And.Contain("platform's own identity");
        result.Items.Should().BeEmpty();
        rig.Factory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Never,
            "a Connection that would sign in as the platform host is never connected, so nothing is enumerated as the host");
        rig.Db.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task I1_a_connection_with_a_user_id_is_still_browsed()
    {
        var rig = NewRig();

        (await rig.Sut.ListSchemasAsync(SqlConnectionRow(), CancellationToken.None)).Outcome.Should().Be(ConnectionToolOutcome.Succeeded);
    }

    // I3: the transport bounds are real -------------------------------------------------------------

    [Fact]
    public void I3_pooling_is_off_the_connect_timeout_is_explicit_and_the_application_is_named_on_a_real_sql_connection()
    {
        var rig = NewRig(new SqlStructureBrowserOptions { ConnectTimeoutSeconds = 7 });
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(
            "Server=db.example;Database=Ops;User Id=reader;Password=p@ss;Pooling=true;Connect Timeout=90;Application Name=other");

        rig.Sut.Bound(connection);

        var bounded = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection.ConnectionString);
        bounded.Pooling.Should().BeFalse("the physical connection closes with the request; the platform keeps no warm login");
        bounded.ConnectTimeout.Should().Be(7);
        bounded.ApplicationName.Should().Be("Apperture structure browser");
        (bounded.DataSource, bounded.InitialCatalog, bounded.UserID, bounded.Password).Should().Be(("db.example", "Ops", "reader", "p@ss"));
        connection.State.Should().Be(System.Data.ConnectionState.Closed, "bounding never opens anything");
    }

    [Fact]
    public void I3_the_defaults_are_fifteen_seconds_for_connect_and_command()
    {
        var options = new SqlStructureBrowserOptions();

        (options.ConnectTimeoutSeconds, options.CommandTimeoutSeconds, options.MaxItems).Should().Be((15, 15, 500));
    }

    [Fact]
    public void I3_bounding_a_non_sql_connection_changes_nothing()
    {
        var rig = NewRig();

        rig.Sut.Invoking(s => s.Bound(rig.Db)).Should().NotThrow();
        rig.Db.ConnectionString.Should().BeEmpty();
    }

    // M1: a resolver's own timeout is not the caller's cancellation --------------------------------

    [Fact]
    public async Task M1_a_secret_resolver_timeout_that_the_caller_did_not_ask_for_is_a_fixed_outcome_not_a_cancellation()
    {
        var rig = NewRig();
        rig.Secrets.Setup(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("vault http timeout"));

        var result = await rig.Sut.ListSchemasAsync(SqlConnectionRow(withSecret: true), CancellationToken.None);

        result.Outcome.Should().Be(ConnectionToolOutcome.SecretsUnavailable);
        result.Message.Should().Be(ConnectionVerificationMessages.SecretResolutionFailed);
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("vault");
    }

    [Fact]
    public async Task M1_the_callers_own_cancellation_while_resolving_secrets_still_propagates()
    {
        using var cts = new CancellationTokenSource();
        var rig = NewRig();
        rig.Secrets.Setup(s => s.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .Returns((SecretRef _, CancellationToken ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult<ResolvedSecret>(null!);
            });

        var act = async () => await rig.Sut.ListSchemasAsync(SqlConnectionRow(withSecret: true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rig.Factory.Verify(f => f.Create(It.IsAny<Connection>(), It.IsAny<ResolvedConnectionMaterial>()), Times.Never);
    }

    // Provider identity -----------------------------------------------------------------------------

    [Fact]
    public void The_provider_serves_platform_sql_and_is_constantly_available()
    {
        var rig = NewRig();

        rig.Sut.ConnectorDefinitionId.Should().Be(WellKnownConnectorDefinitionIds.PlatformSql);
        rig.Sut.Availability.Should().Be(ConnectionToolAvailability.Available);
        rig.Factory.VerifyNoOtherCalls();
        rig.Secrets.VerifyNoOtherCalls();
    }
}
