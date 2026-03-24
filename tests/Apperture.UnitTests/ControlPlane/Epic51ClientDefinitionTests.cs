using Apperture.Connectors.CampusNexus;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 51: Client Definition Model and Module Contribution.
/// </summary>
public sealed class Epic51ClientDefinitionTests
{
    [Fact]
    public async Task CampusNexusClientDefinitionProvider_ReturnsExpectedDefinition()
    {
        var provider = new CampusNexusClientDefinitionProvider();
        var definitions = new List<ClientDefinition>();
        await foreach (var d in provider.GetClientDefinitionsAsync())
            definitions.Add(d);

        definitions.Should().ContainSingle();
        var def = definitions[0];
        def.Id.Value.Should().Be("Apperture.Platform.CampusNexus.Client");
        def.DisplayName.Should().Be("Campus Nexus API Client");
        def.RequiredConnectorDefinitionId.Should().Be(WellKnownConnectorDefinitionIds.PlatformCampusNexus);
        def.ModuleId.Should().BeNull();
        def.UsesGateway.Should().BeFalse();
        def.Operations.Should().HaveCount(2);

        var executeCmd = def.Operations.First(o => o.OperationKey == "ExecuteCommand");
        executeCmd.DisplayName.Should().Be("Execute API Command");
        executeCmd.IsOperatorFacing.Should().BeTrue();
        executeCmd.MayRequireUserInputs.Should().BeTrue();

        var query = def.Operations.First(o => o.OperationKey == "Query");
        query.DisplayName.Should().Be("Query OData");
        query.IsOperatorFacing.Should().BeTrue();
    }

    [Fact]
    public async Task ClientDefinitionQueryService_AggregatesFromMultipleProviders()
    {
        var def1 = new ClientDefinition(
            new ClientDefinitionId("client-a"),
            "Client A",
            null,
            new ConnectorDefinitionId("conn-x"),
            null,
            false,
            []);
        var def2 = new ClientDefinition(
            new ClientDefinitionId("client-b"),
            "Client B",
            null,
            new ConnectorDefinitionId("conn-y"),
            null,
            false,
            []);

        var provider1 = new StubClientDefinitionProvider(def1);
        var provider2 = new StubClientDefinitionProvider(def2);
        var service = new ClientDefinitionQueryService([provider1, provider2]);

        var all = await service.GetAllAsync();
        all.Should().HaveCount(2);
        all.Select(d => d.Id.Value).Should().BeEquivalentTo("client-a", "client-b");
    }

    [Fact]
    public async Task ClientDefinitionQueryService_DedupesById()
    {
        var def = new ClientDefinition(
            new ClientDefinitionId("client-dup"),
            "Client",
            null,
            new ConnectorDefinitionId("conn-x"),
            null,
            false,
            []);
        var provider1 = new StubClientDefinitionProvider(def);
        var provider2 = new StubClientDefinitionProvider(def);
        var service = new ClientDefinitionQueryService([provider1, provider2]);

        var all = await service.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Id.Value.Should().Be("client-dup");
    }

    [Fact]
    public async Task ClientDefinitionQueryService_GetByIdAsync_ReturnsMatch()
    {
        var def = new ClientDefinition(
            new ClientDefinitionId("client-x"),
            "Client X",
            null,
            new ConnectorDefinitionId("conn-x"),
            null,
            false,
            []);
        var service = new ClientDefinitionQueryService([new StubClientDefinitionProvider(def)]);

        var found = await service.GetByIdAsync(new ClientDefinitionId("client-x"));
        found.Should().NotBeNull();
        found!.DisplayName.Should().Be("Client X");

        var notFound = await service.GetByIdAsync(new ClientDefinitionId("client-none"));
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task ClientDefinitionQueryService_EmptyProviders_ReturnsEmptyList()
    {
        var service = new ClientDefinitionQueryService(Array.Empty<IClientDefinitionProvider>());
        var all = await service.GetAllAsync();
        all.Should().BeEmpty();
    }

    private sealed class StubClientDefinitionProvider : IClientDefinitionProvider
    {
        private readonly ClientDefinition _def;

        public StubClientDefinitionProvider(ClientDefinition def) => _def = def;

        public async IAsyncEnumerable<ClientDefinition> GetClientDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return _def;
        }
    }
}
