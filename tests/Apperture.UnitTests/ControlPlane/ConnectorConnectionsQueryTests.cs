using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The narrow "Connections made from one connector" query behind Component usage: the filter is pushed into the
/// repository query, and the management service applies the same <c>connection.read</c> gate as the ordinary list.
/// </summary>
public sealed class ConnectorConnectionsQueryTests
{
    private static AppertureDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppertureDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static ConnectionEntity Row(string id, string connectorId, string? scopeKey = null, int state = 0) =>
        new() { Id = id, ConnectorDefinitionId = connectorId, Name = id, ScopeKey = scopeKey, ResourceContextId = ResourceContextIds.SharedValue, State = state };

    // ---- repository -----------------------------------------------------------------------------

    [Fact]
    public async Task Repository_returns_only_connections_of_the_requested_connector_in_every_state()
    {
        await using var db = CreateDb();
        db.Connections.AddRange(
            Row("a", "Apperture.Platform.Sql"),
            Row("b", "Apperture.Platform.Sql", state: (int)ConnectionState.Retired),
            Row("c", "Apperture.Platform.Sftp"));
        await db.SaveChangesAsync();
        var sut = new SqlConnectionRepository(db);

        var rows = await sut.GetVisibleByConnectorDefinitionAsync(
            ScopeKeyConstants.Instance, new ConnectorDefinitionId("Apperture.Platform.Sql"));

        rows.Select(r => r.Id.Value).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task Repository_keeps_the_scope_containment_visibility_rule()
    {
        await using var db = CreateDb();
        db.Connections.AddRange(
            Row("instance-scoped", "Apperture.Platform.Sql", scopeKey: null),
            Row("module-scoped", "Apperture.Platform.Sql", scopeKey: "module:Some.Module"),
            Row("elsewhere", "Apperture.Platform.Sql", scopeKey: "module:Other"));
        await db.SaveChangesAsync();
        var sut = new SqlConnectionRepository(db);

        var visible = await sut.GetVisibleByConnectorDefinitionAsync(
            "module:Some.Module", new ConnectorDefinitionId("Apperture.Platform.Sql"));

        visible.Select(r => r.Id.Value).Should().Equal("module-scoped");
    }

    [Fact]
    public async Task Repository_default_implementation_filters_the_visible_list_for_test_doubles()
    {
        var repository = new Mock<IConnectionRepository> { CallBase = true };
        repository.Setup(r => r.GetVisibleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>
            {
                new(new ConnectionId("a"), new ConnectorDefinitionId("X"), "a", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null),
                new(new ConnectionId("b"), new ConnectorDefinitionId("Y"), "b", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null),
            });

        var rows = await repository.Object.GetVisibleByConnectorDefinitionAsync("instance", new ConnectorDefinitionId("X"));

        rows.Should().ContainSingle().Which.Id.Value.Should().Be("a");
    }

    // ---- management service ---------------------------------------------------------------------

    private static (ConnectionManagementService Sut, Mock<IConnectionRepository> Repository, Mock<IAuthorizationEvaluationService> Auth) Build(
        bool allowed,
        bool authenticated = true)
    {
        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:test"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(authenticated ? actor.Object : null);
        var auth = new Mock<IAuthorizationEvaluationService>();
        auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        var repository = new Mock<IConnectionRepository>(MockBehavior.Strict);
        var sut = new ConnectionManagementService(
            principal.Object, auth.Object, repository.Object, null!, null!, null!, null!, null!, null!, null!);
        return (sut, repository, auth);
    }

    [Fact]
    public async Task Service_requires_connection_read_at_instance_scope_then_queries_the_connector_filter()
    {
        var (sut, repository, auth) = Build(allowed: true);
        PermissionDescriptor? evaluated = null;
        auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationEvaluationContext, CancellationToken>((c, _) => evaluated = c.Permission)
            .ReturnsAsync(true);
        var id = new ConnectorDefinitionId("Apperture.Platform.Sql");
        repository.Setup(r => r.GetVisibleByConnectorDefinitionAsync(ScopeKeyConstants.Instance, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rows = await sut.ListConnectionsByConnectorAsync(id);

        rows.Should().BeEmpty();
        evaluated!.Action.Should().Be(ActionConstants.ConnectionRead);
        evaluated.Resource!.ResourceType.Should().Be(ResourceTypeConstants.Connection);
        evaluated.Resource.ScopeKey.Should().Be(ScopeKeyConstants.Instance);
        repository.Verify(r => r.GetVisibleByConnectorDefinitionAsync(ScopeKeyConstants.Instance, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A8_service_denies_without_connection_read_and_never_reaches_the_repository()
    {
        var (sut, repository, _) = Build(allowed: false);

        var act = async () => await sut.ListConnectionsByConnectorAsync(new ConnectorDefinitionId("Apperture.Platform.Sql"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Service_denies_an_unauthenticated_caller_and_never_reaches_the_repository()
    {
        var (sut, repository, _) = Build(allowed: true, authenticated: false);

        var act = async () => await sut.ListConnectionsByConnectorAsync(new ConnectorDefinitionId("Apperture.Platform.Sql"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        repository.VerifyNoOtherCalls();
    }
}
