using Apperture.ConnectorResolution;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// An Integration-scoped binding must never be resolved without the Integration identity that
/// disambiguates it.
/// </summary>
/// <remarks>
/// The contextless slot lookup previously returned the first instance-wide match for
/// <c>(ModuleId, SlotName)</c>. With two Integrations of the same module that silently resolves an
/// arbitrary one — and once ResourceContexts exist, potentially another Application's Connection.
/// The lookup is now restricted to bindings that no Integration owns.
/// </remarks>
public sealed class RuntimeSlotResolutionAmbiguityTests
{
    private const string ModuleId = "Sample.Mod";
    private const string SlotName = "Db";
    private const string ContractType = "System.Data.IDbConnection";

    [Fact]
    public async Task ContextlessLookup_NeverReturnsAnIntegrationOwnedBinding()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var (integrationA, _, integrationB, _) = await ArrangeTwoIntegrationsOfTheSameModuleAsync(h);

        var unowned = await h.BindingRepository.GetUnownedBySlotAsync(ModuleId, SlotName);

        unowned.Should().BeNull(
            "both bindings for this slot are Integration-owned, and neither may be selected without an Integration identity");

        // The Integration-scoped lookups still resolve each Integration's own binding exactly.
        var boundA = await h.BindingRepository.GetBySlotAndIntegrationAsync(ModuleId, SlotName, integrationA.Id);
        var boundB = await h.BindingRepository.GetBySlotAndIntegrationAsync(ModuleId, SlotName, integrationB.Id);
        boundA!.ConnectionId!.Value.Should().NotBe(boundB!.ConnectionId!.Value);
    }

    /// <summary>
    /// The regression this epic exists to prevent: two Integrations of the same module must not be able
    /// to resolve each other's Connection.
    /// </summary>
    [Fact]
    public async Task TwoIntegrationsOfTheSameModule_CannotResolveEachOthersConnection()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var (integrationA, connectionA, integrationB, connectionB) = await ArrangeTwoIntegrationsOfTheSameModuleAsync(h);

        var resolver = new RecordingConnectionResolver();
        var slotResolver = new SlotBoundResolver(h.BindingRepository, h.ConnectionRepository, resolver);

        await slotResolver.ResolveSlotAsync<object>(ModuleId, SlotName, integrationA.Id.Value);
        resolver.LastConnectionId.Should().Be(connectionA.Id.Value);

        await slotResolver.ResolveSlotAsync<object>(ModuleId, SlotName, integrationB.Id.Value);
        resolver.LastConnectionId.Should().Be(connectionB.Id.Value);
    }

    [Fact]
    public async Task ResolvingWithoutAnIntegrationIdentity_ReturnsNothingWhenEveryBindingIsOwned()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        await ArrangeTwoIntegrationsOfTheSameModuleAsync(h);

        var resolver = new RecordingConnectionResolver();
        var slotResolver = new SlotBoundResolver(h.BindingRepository, h.ConnectionRepository, resolver);

        var resolved = await slotResolver.ResolveSlotAsync<object>(ModuleId, SlotName);

        resolved.Should().BeNull();
        resolver.LastConnectionId.Should().BeNull("no connection lookup may happen at all");
    }

    /// <summary>
    /// A genuinely global binding — one no Integration owns — remains resolvable without an Integration
    /// identity. That is the only legitimate contextless form, and platform/module operations that rely
    /// on it keep working.
    /// </summary>
    [Fact]
    public async Task GenuinelyGlobalBinding_StillResolvesWithoutAnIntegrationIdentity()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var (_, ownedConnection, _, _) = await ArrangeTwoIntegrationsOfTheSameModuleAsync(h);

        var globalConnection = await CreateConnectionAsync(h, "Global Connection", null);
        await h.BindingRepository.AddAsync(new Binding(
            new BindingId("binding-global"),
            IntegrationId: null,
            ModuleId,
            SlotName,
            ContractType,
            globalConnection.Id,
            CapabilityId: null,
            BindingState.Active));

        var resolver = new RecordingConnectionResolver();
        var slotResolver = new SlotBoundResolver(h.BindingRepository, h.ConnectionRepository, resolver);

        await slotResolver.ResolveSlotAsync<object>(ModuleId, SlotName);

        resolver.LastConnectionId.Should().Be(globalConnection.Id.Value);
        resolver.LastConnectionId.Should().NotBe(ownedConnection.Id.Value);
    }

    private static async Task<(
        Core.Domain.Integration.Integration IntegrationA,
        Connection ConnectionA,
        Core.Domain.Integration.Integration IntegrationB,
        Connection ConnectionB)> ArrangeTwoIntegrationsOfTheSameModuleAsync(ResourceContextOwnershipTestHarness h)
    {
        var appA = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var appB = await h.ResourceContexts.CreateApplicationContextAsync("Application B");

        var integrationA = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments A", ModuleId, ResourceContextId: appA.Id));
        var integrationB = await h.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest("Payments B", ModuleId, ResourceContextId: appB.Id));

        var connectionA = await CreateConnectionAsync(h, "A Connection", appA.Id);
        var connectionB = await CreateConnectionAsync(h, "B Connection", appB.Id);

        await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connectionA.Id, integrationA.Id);
        await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connectionB.Id, integrationB.Id);

        return (integrationA, connectionA, integrationB, connectionB);
    }

    private static async Task<Connection> CreateConnectionAsync(
        ResourceContextOwnershipTestHarness harness,
        string name,
        ResourceContextId? resourceContextId) =>
        await harness.Connections.CreateConnectionAsync(new CreateConnectionRequest(
            new ConnectorDefinitionId("test.connector"),
            name,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: resourceContextId));

    /// <summary>Records which Connection the slot resolver actually reached.</summary>
    private sealed class RecordingConnectionResolver : IConnectionResolver
    {
        public string? LastConnectionId { get; private set; }

        public ValueTask<T?> ResolveAsync<T>(Connection connection, CancellationToken cancellationToken = default) where T : class
        {
            LastConnectionId = connection.Id.Value;
            return ValueTask.FromResult<T?>(null);
        }
    }
}
