using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Connector;

public sealed class ConnectionTests
{
    [Fact]
    public void Connection_implements_IGovernedResource()
    {
        var connDefId = new ConnectorDefinitionId("conn-def-1");
        var connId = new ConnectionId("conn-1");
        var connection = new Connection(
            connId,
            connDefId,
            "Test Connection",
            new Dictionary<string, string> { ["server"] = "localhost" },
            new Dictionary<string, string> { ["password"] = "secret-ref-123" },
            "instance",
            "admin-1");

        var identity = connection.GetIdentity();

        identity.Should().BeOfType<GovernedResourceIdentity>();
        identity.ResourceType.Should().Be(ResourceTypeConstants.Connection);
        identity.ResourceId.Should().Be("conn-1");
        identity.ScopeKey.Should().Be("instance");
        identity.OwnerId.Should().Be("admin-1");
    }

    [Fact]
    public void Connection_implements_IConnectionResource()
    {
        var connection = new Connection(
            new ConnectionId("c1"),
            new ConnectorDefinitionId("cd1"),
            "Conn",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null,
            null);

        connection.Should().BeAssignableTo<IConnectionResource>();
    }
}
