using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The invocation path of the SQL structure browser through its single authorized entry point: both grants first, the
/// Connection loaded from the store, exactly one audit record per remote request (counts only, never content), and the
/// result withheld if the record cannot be written.
/// </summary>
public sealed class ConnectionToolInvocationTests
{
    private static readonly ConnectorDefinitionId Sql = WellKnownConnectorDefinitionIds.PlatformSql;
    private static readonly ConnectorDefinitionId Sftp = WellKnownConnectorDefinitionIds.PlatformSftp;
    private static readonly string[] ReadAndBrowse = [ActionConstants.ConnectionRead, ActionConstants.ConnectionBrowse];

    private static Connection ConnectionFor(ConnectorDefinitionId connector, ConnectionState state = ConnectionState.Active) =>
        new(new ConnectionId("conn-1"), connector, "Warehouse", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null, state)
        {
            ResourceContextId = new Apperture.Core.Domain.ResourceContexts.ResourceContextId("app-a")
        };

    private sealed class AuditSink : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];
        public bool ThrowOnAppend { get; set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            if (ThrowOnAppend)
                throw new InvalidOperationException("Simulated audit sink failure.");
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A browser returning canned structure whose CONTENT is recognisable, so a leak into audit is detectable.</summary>
    private sealed class CannedBrowser(ConnectorDefinitionId connector) : ISqlStructureBrowser
    {
        public List<string> Calls { get; } = [];
        public Func<SqlStructureResult<SqlSchemaInfo>>? Schemas { get; set; }
        public Exception? Throw { get; set; }

        public ConnectorDefinitionId ConnectorDefinitionId => connector;
        public ConnectionToolAvailability Availability => ConnectionToolAvailability.Available;

        public ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken)
        {
            Calls.Add("schemas");
            if (Throw is not null)
                throw Throw;
            return ValueTask.FromResult(Schemas?.Invoke() ?? new SqlStructureResult<SqlSchemaInfo>(
                ConnectionToolOutcome.Succeeded, [new SqlSchemaInfo("hr_secret_schema"), new SqlSchemaInfo("dbo")], false, 500));
        }

        public ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken)
        {
            Calls.Add("tables");
            if (string.IsNullOrWhiteSpace(schema))
                throw new ArgumentException("A name is required.", nameof(schema));
            return ValueTask.FromResult(new SqlStructureResult<SqlTableInfo>(
                ConnectionToolOutcome.Succeeded, [new SqlTableInfo(schema, "salaries", false)], true, 500));
        }

        public ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(Connection connection, string schema, string table, CancellationToken cancellationToken)
        {
            Calls.Add("columns");
            if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(table))
                throw new ArgumentException("A name is required.", nameof(schema));
            return ValueTask.FromResult(new SqlStructureResult<SqlColumnInfo>(
                ConnectionToolOutcome.Succeeded, [new SqlColumnInfo("ssn", "char", false, 1, 11)], false, 500));
        }
    }

    private sealed class Rig
    {
        public required ConnectionToolAccess Sut { get; init; }
        public required Mock<IConnectionRepository> Repository { get; init; }
    }

    private static Rig NewRig(
        Connection? connection, IEnumerable<string> granted, IEnumerable<IConnectionToolProvider> providers, AuditSink sink,
        ConnectionToolRequestLimiter? limiter = null)
    {
        var grants = granted.ToHashSet(StringComparer.Ordinal);
        var auth = new Mock<IAuthorizationEvaluationService>();
        auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationEvaluationContext c, CancellationToken _) => grants.Contains(c.Permission.Action));
        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:operator"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
        var repository = new Mock<IConnectionRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetByIdAsync(new ConnectionId("conn-1"), It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        return new Rig { Sut = new ConnectionToolAccess(principal.Object, auth.Object, repository.Object, providers, sink, limiter ?? new ConnectionToolRequestLimiter()), Repository = repository };
    }

    private static JsonElement Details(AuditRecord record) => JsonDocument.Parse(record.Details!).RootElement;

    [Fact]
    public async Task SF10_one_remote_request_appends_exactly_one_audit_record_with_counts_and_no_content()
    {
        var sink = new AuditSink();
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql)], sink);

        var result = await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        result!.Items.Should().HaveCount(2);
        var record = sink.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be(ConnectionAuditActionNames.StructureBrowsed);
        record.Outcome.Should().Be("succeeded");
        record.ActorId.Value.Should().Be("user:operator");
        record.Resource.Should().BeNull("a non-null Resource would corrupt Details in the SQL audit store");
        record.ResourceContextId.Should().Be("app-a", "the owning context is the loaded Connection's, never inferred");
        var details = Details(record);
        details.GetProperty("resourceType").GetString().Should().Be("Connection");
        details.GetProperty("resourceId").GetString().Should().Be("conn-1");
        details.GetProperty("operation").GetString().Should().Be("schemas");
        details.GetProperty("tool").GetString().Should().Be("sql-structure");
        details.GetProperty("toolOutcome").GetString().Should().Be("Succeeded");
        details.GetProperty("resultCount").GetInt32().Should().Be(2);
        details.GetProperty("truncated").GetBoolean().Should().BeFalse();
        record.Details.Should().NotContain("hr_secret_schema").And.NotContain("dbo\"");
        record.CorrelationId.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SF10_the_selected_names_and_returned_content_are_never_audited_for_tables_and_columns()
    {
        var sink = new AuditSink();
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql)], sink);

        await rig.Sut.ListSqlTablesAsync(new ConnectionId("conn-1"), "payroll");
        await rig.Sut.ListSqlColumnsAsync(new ConnectionId("conn-1"), "payroll", "salaries");

        sink.Records.Select(r => Details(r).GetProperty("operation").GetString()).Should().Equal("tables", "columns");
        foreach (var record in sink.Records)
            record.Details.Should().NotContain("payroll").And.NotContain("salaries").And.NotContain("ssn").And.NotContain("\"char\"");
        Details(sink.Records[0]).GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SF10_a_remote_denial_is_audited_as_failed_with_the_outcome_and_no_remote_text()
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql)
        {
            Schemas = () => new SqlStructureResult<SqlSchemaInfo>(ConnectionToolOutcome.RemoteDenied, [], false, 500, "The database refused this request")
        };
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink);

        var result = await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        result!.Outcome.Should().Be(ConnectionToolOutcome.RemoteDenied);
        var record = sink.Records.Should().ContainSingle().Subject;
        record.Outcome.Should().Be("failed");
        Details(record).GetProperty("toolOutcome").GetString().Should().Be("RemoteDenied");
        record.Details.Should().NotContain("refused");
    }

    [Fact]
    public async Task No_audit_no_data_when_the_audit_record_cannot_be_written()
    {
        var sink = new AuditSink { ThrowOnAppend = true };
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql)], sink);

        var act = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<InvalidOperationException>("the result is dropped, not returned unaudited");
    }

    [Fact]
    public async Task A_provider_failure_is_audited_once_and_the_original_exception_is_preserved()
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql) { Throw = new TimeoutException("db.example timed out") };
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink);

        var act = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<TimeoutException>();
        sink.Records.Should().ContainSingle().Which.Outcome.Should().Be("failed");
        sink.Records[0].Details.Should().NotContain("db.example");
    }

    [Fact]
    public async Task A_cancelled_request_is_recorded_as_cancelled_and_the_cancellation_propagates()
    {
        var sink = new AuditSink();
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql) { Throw = new OperationCanceledException() }], sink);

        var act = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<OperationCanceledException>();
        sink.Records.Should().ContainSingle().Which.Outcome.Should().Be("cancelled");
    }

    [Fact]
    public async Task A_broken_audit_sink_on_a_failure_path_does_not_replace_the_original_exception()
    {
        var sink = new AuditSink { ThrowOnAppend = true };
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql) { Throw = new TimeoutException("slow") }], sink);

        var act = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task A_missing_name_is_rejected_at_the_entry_point_before_the_provider_and_is_not_audited(string schema)
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql);
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink);

        var act = async () => await rig.Sut.ListSqlTablesAsync(new ConnectionId("conn-1"), schema);

        (await act.Should().ThrowAsync<ConnectionToolRequestException>()).Which.Message.Should().Be("The schema name is required.");
        browser.Calls.Should().BeEmpty("the provider is never reached");
        sink.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_over_long_name_is_rejected_at_the_entry_point_with_a_fixed_message_that_does_not_echo_it()
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql);
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink);
        var huge = new string('x', SqlStructureLimits.MaxIdentifierLength + 1);

        var act = async () => await rig.Sut.ListSqlColumnsAsync(new ConnectionId("conn-1"), "dbo", huge);

        var ex = (await act.Should().ThrowAsync<ConnectionToolRequestException>()).Which;
        ex.Message.Should().Be("The table name is at most 128 characters.");
        ex.Message.Should().NotContain(huge);
        browser.Calls.Should().BeEmpty();
        sink.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Name_validation_happens_after_authorization_so_an_unauthorized_caller_learns_nothing_about_the_request()
    {
        var rig = NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionRead], [new CannedBrowser(Sql)], new AuditSink());

        var act = async () => await rig.Sut.ListSqlTablesAsync(new ConnectionId("conn-1"), " ");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task M2_an_argument_exception_raised_by_a_provider_is_an_audited_failure_and_is_never_echoed_by_the_controller()
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql) { Throw = new ArgumentException("remote said: db.example refused Password=hunter2") };
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink);
        var controller = new ConnectionToolsController(rig.Sut);

        var direct = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));
        var viaController = async () => await controller.SqlSchemas("conn-1", CancellationToken.None);

        await direct.Should().ThrowAsync<ArgumentException>();
        sink.Records.Should().ContainSingle().Which.Outcome.Should().Be("failed");
        sink.Records[0].Details.Should().NotContain("hunter2").And.NotContain("db.example");
        // Not a ConnectionToolRequestException, so the controller does not turn it into a 400 that echoes its text.
        var thrown = await viaController.Should().ThrowAsync<ArgumentException>();
        thrown.Which.Should().NotBeOfType<ConnectionToolRequestException>();
    }

    // ---- I2: bounded concurrency ------------------------------------------------------------------------

    private sealed class GatedBrowser(ConnectorDefinitionId connector, Task gate) : ISqlStructureBrowser
    {
        public int Started;
        public ConnectorDefinitionId ConnectorDefinitionId => connector;
        public ConnectionToolAvailability Availability => ConnectionToolAvailability.Available;

        public async ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Started);
            await gate;
            return new SqlStructureResult<SqlSchemaInfo>(ConnectionToolOutcome.Succeeded, [], false, 500);
        }

        public ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(Connection connection, string schema, string table, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task I2_a_third_concurrent_request_for_the_same_connection_is_refused_before_anything_is_sent_and_slots_are_released()
    {
        var gate = new TaskCompletionSource();
        var browser = new GatedBrowser(Sql, gate.Task);
        var sink = new AuditSink();
        var limiter = new ConnectionToolRequestLimiter();
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink, limiter);

        var first = rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask();
        var second = rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask();
        limiter.Running("conn-1").Should().Be(2);
        var third = async () => await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        await third.Should().ThrowAsync<ConnectionToolBusyException>();
        browser.Started.Should().Be(2, "the refused request never reached the provider");
        sink.Records.Should().BeEmpty("nothing has completed yet, and the refused request is not a remote operation");

        gate.SetResult();
        await Task.WhenAll(first, second);
        limiter.Running("conn-1").Should().Be(0);
        sink.Records.Should().HaveCount(2);
        (await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"))).Should().NotBeNull("a released slot admits the next request");
    }

    [Fact]
    public async Task I2_a_failing_request_still_releases_its_slot()
    {
        var limiter = new ConnectionToolRequestLimiter(1);
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql) { Throw = new TimeoutException("slow") }], new AuditSink(), limiter);

        await rig.Sut.Invoking(s => s.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask()).Should().ThrowAsync<TimeoutException>();

        limiter.Running("conn-1").Should().Be(0);
    }

    [Fact]
    public async Task I2_the_limit_is_per_connection_and_the_controller_answers_429_with_retry_after()
    {
        var gate = new TaskCompletionSource();
        var limiter = new ConnectionToolRequestLimiter(1);
        var sink = new AuditSink();
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new GatedBrowser(Sql, gate.Task)], sink, limiter);
        var controller = new ConnectionToolsController(rig.Sut) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };

        var inFlight = rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask();
        var refused = await controller.SqlSchemas("conn-1", CancellationToken.None);

        refused.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
        controller.Response.Headers.RetryAfter.ToString().Should().Be("2");
        limiter.TryEnter("some-other-connection").Should().NotBeNull("another Connection is unaffected");
        gate.SetResult();
        await inFlight;
    }

    [Fact]
    public void The_limiter_counts_and_releases_exactly_once_even_if_a_slot_is_disposed_twice()
    {
        var limiter = new ConnectionToolRequestLimiter(2);

        var a = limiter.TryEnter("c")!;
        var b = limiter.TryEnter("c")!;
        limiter.TryEnter("c").Should().BeNull();
        a.Dispose();
        a.Dispose();
        limiter.Running("c").Should().Be(1);
        limiter.TryEnter("c").Should().NotBeNull();
        b.Dispose();
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("browse-only")]
    [InlineData("none")]
    public async Task A_caller_without_both_grants_never_reaches_the_provider_and_writes_no_audit(string who)
    {
        var granted = who switch
        {
            "read-only" => new[] { ActionConstants.ConnectionRead },
            "browse-only" => new[] { ActionConstants.ConnectionBrowse },
            _ => Array.Empty<string>()
        };
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql);
        var rig = NewRig(ConnectionFor(Sql), granted, [browser], sink);

        await rig.Sut.Invoking(s => s.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await rig.Sut.Invoking(s => s.ListSqlTablesAsync(new ConnectionId("conn-1"), "dbo").AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();
        await rig.Sut.Invoking(s => s.ListSqlColumnsAsync(new ConnectionId("conn-1"), "dbo", "t").AsTask()).Should().ThrowAsync<UnauthorizedAccessException>();

        browser.Calls.Should().BeEmpty();
        sink.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_retired_connection_or_a_connector_without_a_provider_is_not_available_and_writes_no_audit()
    {
        var sink = new AuditSink();
        var retired = NewRig(ConnectionFor(Sql, ConnectionState.Retired), ReadAndBrowse, [new CannedBrowser(Sql)], sink);
        var noProvider = NewRig(ConnectionFor(Sftp), ReadAndBrowse, [new CannedBrowser(Sql)], sink);

        await retired.Sut.Invoking(s => s.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask()).Should().ThrowAsync<ConnectionToolNotAvailableException>();
        await noProvider.Sut.Invoking(s => s.ListSqlSchemasAsync(new ConnectionId("conn-1")).AsTask()).Should().ThrowAsync<ConnectionToolNotAvailableException>();

        sink.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_connection_is_null_and_the_provider_is_never_consulted()
    {
        var browser = new CannedBrowser(Sql);
        var rig = NewRig(null, ReadAndBrowse, [browser], new AuditSink());

        (await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"))).Should().BeNull();
        browser.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Browsing_loads_the_connection_from_the_store_by_id_and_never_trusts_a_supplied_one()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new CannedBrowser(Sql)], new AuditSink());

        await rig.Sut.ListSqlSchemasAsync(new ConnectionId("conn-1"));

        rig.Repository.Invocations.Select(i => i.Method.Name).Should().Equal(nameof(IConnectionRepository.GetByIdAsync));
    }

    // ---- HTTP surface ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_endpoints_map_outcomes_to_status_codes_and_a_remote_denial_is_a_200_with_an_outcome()
    {
        var sink = new AuditSink();
        var browser = new CannedBrowser(Sql)
        {
            Schemas = () => new SqlStructureResponseDtoSource().Denied()
        };
        var controller = new ConnectionToolsController(NewRig(ConnectionFor(Sql), ReadAndBrowse, [browser], sink).Sut);

        var denied = await controller.SqlSchemas("conn-1", CancellationToken.None);
        var badName = await controller.SqlTables("conn-1", "  ", CancellationToken.None);
        var missingSchema = await controller.SqlColumns("conn-1", null, null, CancellationToken.None);

        var ok = denied.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<SqlStructureResponseDto<SqlSchemaDto>>().Subject;
        ok.Outcome.Should().Be("RemoteDenied");
        ok.Message.Should().Be("The database refused this request");
        badName.Result.Should().BeOfType<BadRequestObjectResult>();
        missingSchema.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AZ5_every_structure_endpoint_answers_403_without_browse_and_404_for_an_unknown_connection_or_missing_tool()
    {
        var sink = new AuditSink();
        var withoutBrowse = new ConnectionToolsController(NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionRead], [new CannedBrowser(Sql)], sink).Sut);
        var unknown = new ConnectionToolsController(NewRig(null, ReadAndBrowse, [new CannedBrowser(Sql)], sink).Sut);
        var noTool = new ConnectionToolsController(NewRig(ConnectionFor(Sftp), ReadAndBrowse, [new CannedBrowser(Sql)], sink).Sut);

        (await withoutBrowse.SqlSchemas("conn-1", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
        (await withoutBrowse.SqlTables("conn-1", "dbo", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
        (await withoutBrowse.SqlColumns("conn-1", "dbo", "t", CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
        (await unknown.SqlSchemas("conn-1", CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
        (await noTool.SqlSchemas("conn-1", CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
        sink.Records.Should().BeEmpty();
    }

    [Fact]
    public void The_controller_exposes_only_read_shaped_structure_endpoints_and_no_query_or_mutation_route()
    {
        var actions = typeof(ConnectionToolsController).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);

        actions.Should().OnlyContain(m => m.GetCustomAttributes(typeof(HttpGetAttribute), true).Length == 1,
            "there is no POST, PUT, PATCH or DELETE: nothing here can run SQL or change anything");
        actions.SelectMany(m => m.GetParameters()).Select(p => p.Name).Should().NotContain(n =>
            n == "sql" || n == "statement" || n == "command" || n == "query" || n == "body" || n == "payload");
        actions.Select(m => m.Name).Should().BeEquivalentTo("Get", "SqlSchemas", "SqlTables", "SqlColumns");
    }

    private sealed class SqlStructureResponseDtoSource
    {
        public SqlStructureResult<SqlSchemaInfo> Denied() =>
            new(ConnectionToolOutcome.RemoteDenied, [], false, 500, "The database refused this request");
    }
}
