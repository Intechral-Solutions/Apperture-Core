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
/// Authorized, local-only access to a Connection's tools. Every operation requires connection.read AND connection.browse on
/// the Connection; refusals are identical whether or not the connector has a tool; and answering "which tools?" never touches
/// a client, a secret or the network.
/// </summary>
public sealed class ConnectionToolAccessTests
{
    private static readonly ConnectorDefinitionId Sql = WellKnownConnectorDefinitionIds.PlatformSql;
    private static readonly ConnectorDefinitionId Sftp = WellKnownConnectorDefinitionIds.PlatformSftp;
    private static readonly ConnectorDefinitionId Nexus = WellKnownConnectorDefinitionIds.PlatformCampusNexus;

    private static Connection ConnectionFor(ConnectorDefinitionId connector, ConnectionState state = ConnectionState.Active) =>
        new(new ConnectionId("conn-1"), connector, "Warehouse", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null, state);

    /// <summary>A SQL structure browser that fails the test if it is ever asked to do anything remote.</summary>
    private sealed class TrapSqlBrowser(ConnectorDefinitionId connector, ConnectionToolAvailability availability = ConnectionToolAvailability.Available)
        : ISqlStructureBrowser
    {
        public int AvailabilityReads { get; private set; }

        public ConnectorDefinitionId ConnectorDefinitionId => connector;

        public ConnectionToolAvailability Availability
        {
            get
            {
                AvailabilityReads++;
                return availability;
            }
        }

        public ValueTask<SqlStructureResult<SqlSchemaInfo>> ListSchemasAsync(Connection connection, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider lookup must never reach the remote system.");

        public ValueTask<SqlStructureResult<SqlTableInfo>> ListTablesAsync(Connection connection, string schema, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider lookup must never reach the remote system.");

        public ValueTask<SqlStructureResult<SqlColumnInfo>> ListColumnsAsync(Connection connection, string schema, string table, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider lookup must never reach the remote system.");
    }

    private sealed class MarkerOnlyProvider(ConnectorDefinitionId connector) : IConnectionToolProvider
    {
        public ConnectorDefinitionId ConnectorDefinitionId => connector;
        public ConnectionToolAvailability Availability => ConnectionToolAvailability.Available;
    }

    private sealed class Rig
    {
        public required ConnectionToolAccess Sut { get; init; }
        public required List<PermissionDescriptor> Evaluated { get; init; }
        public required List<AuthorizationEvaluationContext> Contexts { get; init; }
        public required Mock<IConnectionRepository> Repository { get; init; }
    }

    private static Rig NewRig(
        Connection? connection,
        IEnumerable<string> granted,
        IEnumerable<IConnectionToolProvider> providers,
        bool authenticated = true,
        IAuditService? audit = null)
    {
        var grants = granted.ToHashSet(StringComparer.Ordinal);
        var evaluated = new List<PermissionDescriptor>();
        var contexts = new List<AuthorizationEvaluationContext>();
        var auth = new Mock<IAuthorizationEvaluationService>();
        auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationEvaluationContext, CancellationToken>((c, _) =>
            {
                evaluated.Add(c.Permission);
                contexts.Add(c);
            })
            .ReturnsAsync((AuthorizationEvaluationContext c, CancellationToken _) => grants.Contains(c.Permission.Action));

        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:operator"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(authenticated ? actor.Object : null);

        var repository = new Mock<IConnectionRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetByIdAsync(new ConnectionId("conn-1"), It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        return new Rig
        {
            Sut = new ConnectionToolAccess(principal.Object, auth.Object, repository.Object, providers, audit ?? new Mock<IAuditService>().Object, new ConnectionToolRequestLimiter()),
            Evaluated = evaluated,
            Contexts = contexts,
            Repository = repository
        };
    }

    private static readonly string[] ReadAndBrowse = [ActionConstants.ConnectionRead, ActionConstants.ConnectionBrowse];

    // AZ3 / PR1 -------------------------------------------------------------------------------------

    [Fact]
    public async Task AZ3_a_caller_with_read_and_browse_is_offered_the_tool_for_a_sql_connection()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        var tools = await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        tools!.SqlStructureBrowser.Should().BeTrue();
    }

    [Fact]
    public async Task PR1_the_sql_provider_is_matched_by_connector_definition_id()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sftp), new TrapSqlBrowser(Sql)]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.SqlStructureBrowser.Should().BeTrue();
    }

    [Theory]
    [InlineData("sftp")]
    [InlineData("nexus")]
    public async Task PR2_PR3_an_sftp_or_campus_nexus_connection_resolves_no_provider_and_is_still_a_normal_result(string which)
    {
        var connector = which == "sftp" ? Sftp : Nexus;
        var rig = NewRig(ConnectionFor(connector), ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        var tools = await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        tools.Should().NotBeNull();
        tools!.Any.Should().BeFalse();
    }

    [Fact]
    public async Task With_no_providers_at_all_every_connection_simply_has_no_tools()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, []);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.Should().Be(ConnectionToolSet.None);
    }

    [Fact]
    public async Task A_marker_only_provider_does_not_make_a_typed_tool_available()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new MarkerOnlyProvider(Sql)]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.SqlStructureBrowser.Should().BeFalse();
    }

    // AZ2 / AZ4 / AZ5 ------------------------------------------------------------------------------

    [Fact]
    public async Task AZ2_a_caller_with_only_connection_read_is_refused_the_tools()
    {
        var rig = NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionRead], [new TrapSqlBrowser(Sql)]);

        var act = async () => await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AZ4_connection_browse_without_connection_read_grants_nothing()
    {
        var rig = NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionBrowse], [new TrapSqlBrowser(Sql)]);

        var act = async () => await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>("aggregation never grants access: read is still required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("connection.update")]
    [InlineData("connection.create,connection.update,connection.delete")]
    public async Task No_other_connection_permission_stands_in_for_browse(string grantedCsv)
    {
        var granted = grantedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var rig = NewRig(ConnectionFor(Sql), granted.Append(ActionConstants.ConnectionRead), [new TrapSqlBrowser(Sql)]);

        var act = async () => await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Both_grants_are_evaluated_on_the_connection_at_instance_scope()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        rig.Evaluated.Select(p => p.Action).Should().Equal(ActionConstants.ConnectionRead, ActionConstants.ConnectionBrowse);
        rig.Evaluated.Should().OnlyContain(p => p.Resource!.ResourceType == ResourceTypeConstants.Connection);
        rig.Evaluated.Should().OnlyContain(p => p.Resource!.ScopeKey == ScopeKeyConstants.Instance);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sql)], authenticated: false);

        var act = async () => await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        rig.Evaluated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_scoped_connection_is_authorized_on_its_own_identity_exactly_as_reading_it_is()
    {
        var scoped = new Connection(
            new ConnectionId("conn-1"), Sql, "Warehouse", new Dictionary<string, string>(), new Dictionary<string, string>(),
            ScopeKey: "module:sample.mod", OwnerId: null);
        var rig = NewRig(scoped, ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        rig.Contexts.Should().HaveCount(2);
        rig.Contexts.Should().OnlyContain(c => c.Permission.Resource!.ScopeKey == "module:sample.mod");
        rig.Contexts.Should().OnlyContain(c => c.ResourceIdentity != null && c.ResourceIdentity.ScopeKey == "module:sample.mod" && c.ResourceIdentity.ResourceId == "conn-1");
    }

    // AZ7 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AZ7_the_refusal_is_identical_and_the_provider_is_never_consulted_for_a_caller_without_browse()
    {
        var provider = new TrapSqlBrowser(Sql);
        var withProvider = NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionRead], [provider]);
        var withoutProvider = NewRig(ConnectionFor(Sftp), [ActionConstants.ConnectionRead], [provider]);

        var a = await Record.ExceptionAsync(async () => await withProvider.Sut.GetToolsAsync(new ConnectionId("conn-1")));
        var b = await Record.ExceptionAsync(async () => await withoutProvider.Sut.GetToolsAsync(new ConnectionId("conn-1")));

        a.Should().BeOfType<UnauthorizedAccessException>();
        b.Should().BeOfType<UnauthorizedAccessException>();
        a!.Message.Should().Be(b!.Message, "a caller cannot tell a connector with a tool from one without");
        provider.AvailabilityReads.Should().Be(0, "availability is not even read until both grants are held");
    }

    [Fact]
    public async Task AZ5_the_endpoint_re_evaluates_server_side_and_answers_403_without_browse()
    {
        var rig = NewRig(ConnectionFor(Sql), [ActionConstants.ConnectionRead], [new TrapSqlBrowser(Sql)]);
        var controller = new ConnectionToolsController(rig.Sut);

        var result = await controller.Get("conn-1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task The_endpoint_answers_200_with_typed_flags_for_an_authorized_caller_and_404_for_an_unknown_connection()
    {
        var known = new ConnectionToolsController(NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sql)]).Sut);
        var unknown = new ConnectionToolsController(NewRig(null, ReadAndBrowse, [new TrapSqlBrowser(Sql)]).Sut);

        var ok = await known.Get("conn-1", CancellationToken.None);
        var missing = await unknown.Get("conn-1", CancellationToken.None);

        ok.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(new ConnectionToolsDto(true));
        missing.Result.Should().BeOfType<NotFoundResult>();
    }

    // Lifecycle / PR4 / LC3 -------------------------------------------------------------------------

    [Fact]
    public async Task LC1_a_retired_connection_offers_no_tools_and_does_not_consult_the_provider()
    {
        var provider = new TrapSqlBrowser(Sql);
        var rig = NewRig(ConnectionFor(Sql, ConnectionState.Retired), ReadAndBrowse, [provider]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.Should().Be(ConnectionToolSet.None);
        provider.AvailabilityReads.Should().Be(0);
    }

    [Fact]
    public async Task LC3_an_unavailable_provider_leaves_the_base_workspace_unaffected()
    {
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [new TrapSqlBrowser(Sql, ConnectionToolAvailability.Unavailable)]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.Should().Be(ConnectionToolSet.None);
    }

    [Fact]
    public async Task PR4_an_unknown_connector_falls_back_safely_to_no_tools()
    {
        var rig = NewRig(ConnectionFor(new ConnectorDefinitionId("Gone.Connector")), ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1")))!.Should().Be(ConnectionToolSet.None);
    }

    [Fact]
    public async Task A_missing_connection_is_null_not_an_exception()
    {
        var rig = NewRig(null, ReadAndBrowse, [new TrapSqlBrowser(Sql)]);

        (await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"))).Should().BeNull();
    }

    // PR5 / performance -----------------------------------------------------------------------------

    [Fact]
    public void PR5_the_access_component_cannot_reach_a_client_a_resolver_or_a_secret()
    {
        var dependencies = typeof(ConnectionToolAccess).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        dependencies.Should().BeEquivalentTo(
        [
            typeof(IPrincipalContext), typeof(IAuthorizationEvaluationService), typeof(IConnectionRepository),
            typeof(IEnumerable<IConnectionToolProvider>), typeof(IAuditService), typeof(ConnectionToolRequestLimiter)
        ], "no connection resolver, client factory, secret resolver or secret store is even injectable, so provider lookup cannot do I/O");
    }

    [Fact]
    public async Task Answering_which_tools_touches_only_the_connection_lookup_and_authorization()
    {
        var provider = new TrapSqlBrowser(Sql);
        var rig = NewRig(ConnectionFor(Sql), ReadAndBrowse, [provider]);

        await rig.Sut.GetToolsAsync(new ConnectionId("conn-1"));

        rig.Repository.Invocations.Select(i => i.Method.Name).Should().Equal(nameof(IConnectionRepository.GetByIdAsync));
        rig.Evaluated.Should().HaveCount(2);
    }
}
