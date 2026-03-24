using System.Linq;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 52A: Catalog read must evaluate <c>connector.definition.read</c>, not <c>connector.manage</c>.
/// </summary>
public sealed class ConnectorDefinitionCatalogReadServiceTests
{
    [Fact]
    public async Task ListAsync_EvaluatesConnectorDefinitionRead_OnConnectorDefinitionInstanceScope()
    {
        var def = new ConnectorDefinition(WellKnownConnectorDefinitionIds.PlatformSql, "SQL", null, new Dictionary<string, string>());
        var registry = new StubRegistry([def]);
        PermissionDescriptor? captured = null;
        var auth = new CapturingAuthorizationEvaluationService(p => captured = p, allow: true);
        var principal = new StubPrincipalContext();
        var sut = new ConnectorDefinitionCatalogReadService(principal, auth, registry);

        var list = await sut.ListAsync(CancellationToken.None);

        list.Should().ContainSingle();
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(ActionConstants.ConnectorDefinitionRead);
        captured.Resource!.ResourceType.Should().Be(ResourceTypeConstants.ConnectorDefinition);
        captured.Resource.ScopeKey.Should().Be(ScopeKeyConstants.Instance);
    }

    [Fact]
    public async Task ListAsync_WhenAuthorizationDenies_ThrowsUnauthorizedAccessException()
    {
        var def = new ConnectorDefinition(WellKnownConnectorDefinitionIds.PlatformSql, "SQL", null, new Dictionary<string, string>());
        var registry = new StubRegistry([def]);
        var auth = new CapturingAuthorizationEvaluationService(_ => { }, allow: false);
        var principal = new StubPrincipalContext();
        var sut = new ConnectorDefinitionCatalogReadService(principal, auth, registry);

        var act = async () => await sut.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private sealed class StubRegistry : IConnectorDefinitionRegistry
    {
        private readonly IReadOnlyList<ConnectorDefinition> _definitions;

        public StubRegistry(IReadOnlyList<ConnectorDefinition> definitions) => _definitions = definitions;

        public void Register(ConnectorDefinition definition) { }

        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) =>
            moduleId is null
                ? _definitions
                : _definitions.Where(d => d.ModuleId == moduleId).ToList();

        public ConnectorDefinition? Get(ConnectorDefinitionId id) =>
            _definitions.FirstOrDefault(d => d.Id.Value == id.Value);
    }

    private sealed class CapturingAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly Action<PermissionDescriptor> _onEvaluate;
        private readonly bool _allow;

        public CapturingAuthorizationEvaluationService(Action<PermissionDescriptor> onEvaluate, bool allow)
        {
            _onEvaluate = onEvaluate;
            _allow = allow;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            _onEvaluate(context.Permission);
            return ValueTask.FromResult(_allow);
        }
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("user:test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test";
    }
}
