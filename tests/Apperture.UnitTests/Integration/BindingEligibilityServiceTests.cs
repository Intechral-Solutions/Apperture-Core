using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class BindingEligibilityServiceTests
{
    [Fact]
    public async Task GetEligibleConnectionsAsync_FiltersByContractFactoryCompatibility()
    {
        var active = new[]
        {
            new Connection(
                new ConnectionId("conndefok00000000000001"),
                new ConnectorDefinitionId("def.compatible"),
                "Compatible",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                null,
                null,
                ConnectionState.Active),
            new Connection(
                new ConnectionId("connincompat000000000001"),
                new ConnectorDefinitionId("def.incompatible"),
                "Incompatible",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                null,
                null,
                ConnectionState.Active)
        };

        var connections = new Mock<IConnectionRepository>();
        connections.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);

        var registry = new Mock<IConnectorDefinitionRegistry>();
        registry.Setup(x => x.Get(new ConnectorDefinitionId("def.compatible")))
            .Returns(new ConnectorDefinition(new ConnectorDefinitionId("def.compatible"), "A", null, new Dictionary<string, string>()));
        registry.Setup(x => x.Get(new ConnectorDefinitionId("def.incompatible")))
            .Returns(new ConnectorDefinition(new ConnectorDefinitionId("def.incompatible"), "B", null, new Dictionary<string, string>()));

        var factories = new Mock<IConnectorClientFactoryRegistry>();
        factories
            .Setup(x => x.GetFactory<TestContract>(It.IsAny<ConnectorDefinitionId>()))
            .Returns((ConnectorDefinitionId id) =>
                string.Equals(id.Value, "def.compatible", StringComparison.Ordinal)
                    ? new TestContractFactory()
                    : null);

        var sut = new BindingEligibilityService(connections.Object, registry.Object, factories.Object);

        var eligible = await sut.GetEligibleConnectionsAsync(
            moduleId: "M",
            slotName: "slot",
            contractType: typeof(TestContract).FullName!,
            owningResourceContextId: null,
            CancellationToken.None);

        eligible.Should().ContainSingle();
        eligible[0].ConnectorDefinitionId.Value.Should().Be("def.compatible");
    }

    [Fact]
    public async Task GetEligibleConnectionsAsync_WithoutFactoryRegistry_RemainsPermissiveForActiveKnownDefinitions()
    {
        var active = new[]
        {
            new Connection(
                new ConnectionId("conndef0000000000000001"),
                new ConnectorDefinitionId("def.any"),
                "Any",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                null,
                null,
                ConnectionState.Active)
        };

        var connections = new Mock<IConnectionRepository>();
        connections.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);

        var registry = new Mock<IConnectorDefinitionRegistry>();
        registry.Setup(x => x.Get(new ConnectorDefinitionId("def.any")))
            .Returns(new ConnectorDefinition(new ConnectorDefinitionId("def.any"), "Any", null, new Dictionary<string, string>()));

        var sut = new BindingEligibilityService(connections.Object, registry.Object);

        var eligible = await sut.GetEligibleConnectionsAsync(
            moduleId: "M",
            slotName: "slot",
            contractType: typeof(TestContract).FullName!,
            owningResourceContextId: null,
            CancellationToken.None);

        eligible.Should().ContainSingle();
    }

    private sealed class TestContract;

    private sealed class TestContractFactory : IConnectorClientFactory<TestContract>
    {
        public TestContract Create(Connection connection, ResolvedConnectionMaterial material) => new();
    }
}
